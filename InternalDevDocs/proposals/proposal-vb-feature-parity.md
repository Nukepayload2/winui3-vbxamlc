# Proposal: VB 生成器特色对齐（入口点初始化 / UnhandledException / DesignerGenerated / WithEvents）

- **状态**：* [x] Proposed
- **Prototype**：`[ ]`
- **Implementation**：`[ ]`
- **Specification**：`[ ]`

## Summary

微软在 WinUI3 XAML 编译器里做了一半的 VB 支持：VB 生成器骨架已存在且随 C# 同步（`WithEvents`、`DesignerGenerated`、`UnhandledException` 调试挂钩均已输出），但相对 C# 的入口点初始化不完整，且存在 VB 特定映射缺口。本提案对齐 4 个 VB 特色域与 C# 功能，并充分考虑 VB 语言特色。

4 个特色域：

1. **入口点与初始化**——VB 不依赖 dll 式 `Main`，采用 `Sub Main` 的 module 的 `Sub New` 做特殊初始化（对齐 C# `Main` 里的 ComWrappers / XamlOptionalChanges / 同步上下文 / STAThread）。
2. **Application UnhandledException**——VB 在应用程序类发 unhandled exception 事件（C# 已在 `InitializeComponent` 输出 debug-only break，VB 需对齐并联动入口初始化）。
3. **DesignerGenerated 特性**——VB 有 `DesignerGeneratedAttribute`，VB 编译器据此自动钩 `InitializeComponent`（已输出，需验证并清理遗留）。
4. **code-behind x:Name 映射 WithEvents**——用户用 `Handles` 写事件处理器（已输出，需修 `x:FieldModifier` 的 VB 关键字映射缺口）。

## Motivation

接手「微软做了一半的 WinUI3 VB XAML 编译器」后，VB 生成器的相对 C# 缺口集中在**入口点初始化**与**VB 关键字映射**：

- C# `Program.Main` 已实现（`CSharpAppPass1.tt:22-67`）：`[STAThread]`（`IsWin32App` 时）、`ComWrappersSupport.InitializeComWrappers()`（`UsingCSWinRT` 时）、`XamlOptionalChanges` 启停、`Application.Start` lambda 内设 `DispatcherQueueSynchronizationContext`。
- VB `Program.Main`（`VisualBasicAppPass1.tt:21-37`）**全部缺失**：无条件 `<MTAThread()>`、无 ComWrappers 初始化、无 XamlOptionalChanges、Start lambda 内无同步上下文设置。
- `DISABLE_XAML_GENERATED_MAIN` 下：C# 生成 `XamlGeneratedProgram.XamlGeneratedMain()` + `XamlGeneratedCreateApplicationInstance()`（`CSharpAppPass1.tt:25,36,55`），VB **不生成任何启动入口类**（`#If Not DISABLE_XAML_GENERATED_MAIN` 包住整个 Program 类）——自定义入口的 VB 项目无任何 XAML 启动入口。
- `x:FieldModifier="internal"` 在 VB 侧输出 `Internal`（`FieldDefinition.cs:53` `modifier.ToTitleCase()`），但 VB 关键字是 `Friend`——生成代码不可编译。

这些缺口使 VB WinUI3 应用无法正确初始化 WinRT COM、无法启用可选特性、异常跨线程冒泡不可靠、自定义入口无路可走。端到端从未验证（`docs/design-notes/xamlcompiler.md` 自述 unit test projects currently broken）。

## Detailed design

### 1. 入口点与初始化（`VisualBasicAppPass1.tt`）

**C# 参考**（`CSharpAppPass1.tt:30-67`）：

```csharp
[STAThread] // 仅 IsWin32App
static void Main(string[] args)
{
    WinRT.ComWrappersSupport.InitializeComWrappers(); // 仅 UsingCSWinRT
    XamlOptionalChanges.EnableChange(...);            // Enabled/Disabled 遍历
    Application.Start((p) => {
        var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
        SynchronizationContext.SetSynchronizationContext(context);
        new App();
    });
}
```

**VB 现状**（`VisualBasicAppPass1.tt:21-37`）：

```vb
<MTAThread()> _          ' 无条件，WinUI3 桌面应为 STAThread 且按 IsWin32App 条件化
Shared Sub Main(ByVal args() As String)
    Microsoft.UI.Xaml.Application.Start(Function(p) New App())   ' 无同步上下文、无 ComWrappers、无 XamlOptionalChanges
End Sub

Sub Program   ' 遗留：非构造方法，不服务任何用途，建议删除
End Sub
```

**设计 A（推荐）**：VB-idiomatic `Module Program`，`Sub New` 做特殊初始化，`Sub Main` 只负责启动：

```vb
Public Module Program
    Sub New()
        ' Module Sub New 先于 Sub Main 运行：特殊初始化
#If Not DISABLE_XAML_GENERATED_MAIN Then
#If UsingCSWinRT ... ' 由代码生成器按 ProjectInfo.UsingCSWinRT 输出
        WinRT.ComWrappersSupport.InitializeComWrappers()
#End If
        XamlOptionalChanges.EnableChange(...) / .DisableChange(...)
#End If
    End Sub

    <STAThread()> _     ' 仅 IsWin32App 输出（对齐 C#）
    Shared Sub Main(ByVal args() As String)
        Microsoft.UI.Xaml.Application.Start(
            Sub(p)
                Dim context As New Global.Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Global.Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread())
                Global.System.Threading.SynchronizationContext.SetSynchronizationContext(context)
                New Global.<AppShortName>()()
            End Sub)
    End Sub
End Module
```

**设计 B（备选）**：保持 `Class Program`，把初始化移入 `Shared Sub New`（首次访问 Program 时运行，也在 `Shared Sub Main` 之前）。改动面更小，但不贴合「module 的 sub new」这一 VB 习惯表述。

**`DISABLE_XAML_GENERATED_MAIN` 的 VB 等价**（对齐 `CSharpAppPass1.tt:78-89`）：VB 侧也应在 `#If DISABLE_XAML_GENERATED_MAIN` 分支生成可被用户自定义入口调用的启动钩子，用 VB partial method 模式：

```vb
Partial Class <App>
    Shared Sub XamlGeneratedCreateApplicationInstance()
        _XamlGeneratedCreateApplicationInstance()
    End Sub
    Private Partial Sub _XamlGeneratedCreateApplicationInstance()
    End Sub
End Class
```

主体在 pass2 由 VB PagePass2 仅在 App 声明无参构造时补全（镜像 C# 注释机制，`CSharpAppPass1.tt:74-77`）。生成器改动点：`VisualBasicAppPass1.tt` / `VisualBasicAppPass1.cs`、`VisualBasicPagePass2.tt`（App 无参构造检测）。

### 2. Application UnhandledException

**C# 参考**（`CSharpAppPass1.tt:127-132`，`InitializeComponent` 内）：

```csharp
#if DEBUG && !DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION
    UnhandledException += (sender, e) => {
        if (Debugger.IsAttached) Debugger.Break();
    };
#endif
```

**VB 现状**（`VisualBasicAppPass1.tt:76-83`）**已等价输出**：

```vb
#If Debug AndAlso Not DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION Then
        AddHandler Me.UnhandledException,
            Sub(sender As Object, unhandledExceptionArgs As UnhandledExceptionEventArgs)
                If Global.System.Diagnostics.Debugger.IsAttached Then
                    Global.System.Diagnostics.Debugger.Break()
                End If
            End Sub
#End If
```

**设计**：

- 保留现有 `AddHandler Me.UnhandledException` 调试挂钩（已对齐 C#，不删）。
- 联动第 1 条：入口初始化补上 `DispatcherQueueSynchronizationContext` 后，后台线程异常才正确冒泡到 `Application.UnhandledException`——否则 handler 收不到跨线程异常。
- 公开面不改：VB 用户照常 `AddHandler Me.UnhandledException, AddressOf ...` 或 `AddHandler Application.Current.UnhandledException, ...` 注册自己的处理。生成代码不替用户吞异常。

### 3. DesignerGenerated 特性

**VB 现状**：App（`VisualBasicAppPass1.tt:40`）与 Page（`VisualBasicPagePass1.tt:23`）均已输出：

```vb
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>  _
Partial Class <Page/App>
    ...
    Public Sub InitializeComponent()
```

**VB 编译器机制**（推测，需实证）：带 `DesignerGeneratedAttribute` 的 partial 类，vbc 生成 `Sub InitializeInstance()` 并在每个构造器开头注入调用；`InitializeInstance` 内部调用 `InitializeComponent()`。因此 VB 用户的构造器可不显式调 `InitializeComponent` 仍能加载 XAML。

**设计**：

- 保留特性与 `Public Sub InitializeComponent()` + `_contentLoaded` 守卫的组合（自动钩与显式调用叠加时守卫防重复加载）。
- **实证项**：建最小 WinUI3 VB 页面/应用，验证（a）用户构造器不调 `InitializeComponent` 是否仍加载（DesignerGenerated 自动钩是否生效）；（b）与 `_contentLoaded` 守卫协同无重复加载。若自动钩不生效，需评估是否调整 `InitializeComponent` 可见性或在生成代码补构造器调用。
- **清理**：删除 App 里遗留的 `Sub Program`（`VisualBasicAppPass1.tt:33`）——它不是构造方法，污染公开 API 面，违背 VB「不引入第二种做事方式」原则。

### 4. code-behind x:Name 映射 WithEvents

**VB 现状**（`VisualBasicPagePass1.tt:98`）：

```vb
<Global.System.CodeDom.Compiler.GeneratedCodeAttribute(...)>  _
<#=fieldData.FieldModifier#> WithEvents <#=fieldData.FieldName#> As <#=Globalize(fieldData.FieldTypeName)#>
```

即 `Private WithEvents Button1 As Global.Microsoft.UI.Xaml.Controls.Button`，用户可写 `Sub Handler(...) Handles Button1.Click`。

**缺口：`x:FieldModifier` VB 关键字映射**。`FieldDefinition.cs:45-55` 对 VB 用 `modifier.ToTitleCase()`：

| `x:FieldModifier` 值 | C# 输出 | VB 现状（ToTitleCase） | VB 正确关键字 |
|---------------------|---------|------------------------|---------------|
| `public` | `public` | `Public` | `Public` |
| `internal` | `internal` | `Internal` **（无效）** | `Friend` |
| `protected` | `protected` | `Protected` | `Protected` |
| `private` | `private` | `Private` | `Private` |

**设计**：为 `LanguageSpecificString` 的 FieldModifier 增加 VB 专用映射（`FieldDefinition.cs` 两个构造函数里的 `_fieldModifier`）：`internal`→`Friend`，其余保持标题化。预期输出 `Friend WithEvents Button1 As ...`，与 `Friend`（internal）字段配合跨程序集访问。

**边界（WithEvents 适用性）**：`WithEvents` 仅对声明类类型且带可钩事件的字段合法。若 `x:Name` 字段类型是值类型 / 无事件的接口，`WithEvents` 会让 vbc 报错。设计：按 `FieldType` 判断，不支持 `WithEvents` 的类型回退为普通 `Friend/Private` 字段（保留 `Handles` 不可用的可接受降级）。需要实证哪些 XAML 类型会命中该边界。

## Drawbacks

- **设计 A 的 Module 改动**：现有 VB WinUI3 用户若已在工程里设自定义启动对象（自定义 `Sub Main`），生成 `Module Program` 会与既有启动对象并存；需定义覆盖/禁用规则（`DISABLE_XAML_GENERATED_MAIN` 已为此存在，但要确认 VB 侧用法）。
- **WithEvents 回退逻辑**：为无事件类型做降级分支，增加 `FieldDefinition` / 模板复杂度。
- **实证成本**：DesignerGenerated 自动钩与 WithEvents 边界都需最小应用实证，涉及本地构建 WinUI3 VB 工程（首次搭建成本）。

## Alternatives

- **设计 B**：保持 `Class Program` + `Shared Sub New`，最小改动补齐初始化调用（ComWrappers / XamlOptionalChanges / 同步上下文 / STAThread），不引入 Module。牺牲 VB「module sub new」习惯表述，但兼容面最小。
- **入口点不补**：只修 `x:FieldModifier` 映射与 `Sub Program` 清理，入口点初始化缺口留到单独提案。风险：VB 应用在当前 WinUI3 下 WinRT COM / 可选特性/跨线程异常本就不可靠，推迟修复会延续半成品状态。
- **WithEvents 不改**：保留 `Private WithEvents` 默认，仅文档化 `x:FieldModifier="internal"` 不可用于 VB。不推荐——生成代码不可编译是硬伤。

## Unresolved questions

1. `DesignerGeneratedAttribute` + `InitializeInstance` 自动钩在 WinUI3（.NET SDK / CSWinRT）下是否生效？——**待实证**（最小应用测试）。
2. `Module Program` 改动对现有 VB 工程启动对象设置的兼容策略？（覆盖规则、`DISABLE_XAML_GENERATED_MAIN` 语义确认）——**待定**。
3. `WithEvents` 边界：哪些 XAML `x:Name` 字段类型会命中「非类 / 无事件」降级分支？——**待实证**（枚举类型面）。
4. `MTAThread`→`STAThread` 切换是否影响现有 VB 应用？（推测 MTA 在 WinUI3 桌面不正确，但需实证）。
5. `Sub Program` 的删除是否有隐藏依赖？（已检查：非构造、无引用，但需跑测试确认）
