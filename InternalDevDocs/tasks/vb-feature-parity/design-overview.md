# 总体设计：VB 生成器特色对齐

- **状态**：plan（概要设计，逐文件改动见 `design-detailed.md`）
- **权威**：`InternalDevDocs/meetings/meeting-vb-feature-parity.md` RESOLUTION

## 1. 背景与现状（四域诊断，已核对）

接手基线的 4 个缺口全部属实（对照源码逐行复核）：

| 域 | 现状（证据锚点） | C# 基准 |
|----|------------------|---------|
| 入口点与初始化 | `VisualBasicAppPass1.tt:21-37`：`Class Program` 整段被 `#If Not DISABLE_XAML_GENERATED_MAIN` 包住；`:24` 无条件 `<MTAThread()>`；`:27-29` `Shared Sub Main` 直接 `Application.Start(Function(p) New Global.<FullName>())`，无 ComWrappers / XamlOptionalChanges / 同步上下文；`:33` 遗留 `Sub Program` | `CSharpAppPass1.tt:22-67`：`[STAThread]` 仅 `IsWin32App`（`:31-33`）、`InitializeComWrappers()` 仅 `UsingCSWinRT`（`:39-41`）、两遍历 `XamlOptionalChanges`（`:42-47`）、Start lambda 内同步上下文（`:48-57`）；DISABLE 改名 `XamlGeneratedProgram` + `XamlGeneratedMain`（`:25,36`）+ `XamlGeneratedCreateApplicationInstance` + `static partial void`（`:78-89`） |
| UnhandledException | `VisualBasicAppPass1.tt:76-83` 已等价输出 `AddHandler Me.UnhandledException, Sub(...) Debugger.Break()`，条件 `#If Debug AndAlso Not DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION` 正确 | `CSharpAppPass1.tt:127-132`（`+=` 版） |
| DesignerGenerated | `VisualBasicAppPass1.tt:40`、`VisualBasicPagePass1.tt:23` 已输出 `<DesignerGenerated()>`；`InitializeComponent` 均 `Public` 且带 `_contentLoaded` 守卫（`:60-64` / `:59-62`）；`:33` 遗留 `Sub Program` | `CSharpAppPass1.tt:105-133` |
| WithEvents / x:FieldModifier | `VisualBasicPagePass1.tt:98` 输出 `<#=fieldData.FieldModifier#> WithEvents ...`；`FieldDefinition.cs:53` VB 槽位 `modifier.ToTitleCase()` 使 `internal`→`Internal`（非 VB 关键字，vbc 必错）；`:85` 四语言同串路径同样输出小写 `internal`/`public` | `CSharpPagePass1.tt:104` 输出小写 `fieldData.FieldModifier`（`internal` 合法） |

历史 master 佐证 `Friend` 是正确关键字：`src/XamlCompiler/TestMasters/RegressionProjects/Basic/VisualBasic/Simple/obj/x86/Debug/FieldModifierTests.g.i.vb:41` 为 `Friend WithEvents Internal1 As ...`（UWP 时代 `x:FieldModifier="internal"` 的既有输出）；`MainPage.g.i.vb:23` 为 `private WithEvents ...`（默认小写）。

## 2. 设计方向（设计 A 采纳）

入口容器从 `Class Program` 改为 `Module Program`（VB 入口惯例，容器不可实例化、建模更准）。**初始化内联于 `Sub Main`/`XamlGeneratedMain` 方法体**（对齐 C# `CSharpAppPass1.tt:30-67`，两 DISABLE 分支共享同一段初始化），**不再由 `Sub New` 承担**——`Sub New` + `#If Not DISABLE` 的归属割裂会让 DISABLE 用户裸奔。实测（C# 老登，dotnet 10 / Roslyn VB）：Module `Sub Main`（无 `Shared`）+ `<STAThread()>` 编译运行正确；Module 成员隐式 Shared 语义成立。

### 2.1 域 1：入口点与初始化

**非 DISABLE 形态（期望生成代码，VB）**：

```vb
Namespace Global.<AppNamespace>

Public Module Program

    ' 不写线程模型特性——VB 编译器对入口点默认注入 STAThreadAttribute（P0-3 实证），删除了旧模板无条件 <MTAThread()>（把默认 STA 覆盖成 MTA）
    <Global.System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler", " ...")>  _
    <Global.System.Diagnostics.DebuggerNonUserCodeAttribute()>  _
    Sub Main(ByVal args() As String)   ' Module 成员隐式 Shared，不写 Shared
        ' 初始化内联（弃 Sub New 承担，DISABLE 分支共享同一段）：
        ' 仅 UsingCSWinRT（T4 时条件）：
        Global.WinRT.ComWrappersSupport.InitializeComWrappers()
        ' Enabled/DisabledXamlOptionalChanges 各遍历一条（T4 时）：
        Global.Microsoft.UI.Xaml.Settings.XamlOptionalChanges.EnableChange(Global.Microsoft.UI.Xaml.Settings.XamlChangeId.<ChangeName>)
        Global.Microsoft.UI.Xaml.Settings.XamlOptionalChanges.EnableChange(CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId))
        Global.Microsoft.UI.Xaml.Settings.XamlOptionalChanges.DisableChange(...)
        ' 仅 UsingCSWinRT（T4 时条件）：
        Global.Microsoft.UI.Xaml.Application.Start(
            Sub(p)
                Dim context As New Global.Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Global.Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread())
                Global.System.Threading.SynchronizationContext.SetSynchronizationContext(context)
                Dim _application As New Global.<AppFullName>()
            End Sub)
        ' 非 UsingCSWinRT（T4 else 分支）：
        Global.Microsoft.UI.Xaml.Application.Start(
            Sub(p)
                Dim _application As New Global.<AppFullName>()
            End Sub)
    End Sub

End Module

End Namespace
```

**C# 对照基准**（`CSharpAppPass1.tt:22-67`）：

```csharp
[STAThread]                                  // 仅 IsWin32App
static void Main(string[] args)
{
    global::WinRT.ComWrappersSupport.InitializeComWrappers();   // 仅 UsingCSWinRT
    global::Microsoft.UI.Xaml.Settings.XamlOptionalChanges.EnableChange(...);  // 两遍历
    global::Microsoft.UI.Xaml.Application.Start((p) => {
        var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
            global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        new App();
    });
}
```

要点（RESOLUTION 硬性裁决）：

- **Module 内 `Sub Main` 不写 `Shared`**（BC30433 实测）。
- **`#If UsingCSWinRT` 是伪代码**：`UsingCSWinRT`/`IsWin32App` 是 `XamlProjectInfo` 属性（`XamlProjectInfo.cs:118-120`，由 `CompileXamlInternal.cs:330` 与 `:1913-1914` 赋值），不是 VB 条件编译符号。模板必须 T4 时条件（`<# if (ProjectInfo.UsingCSWinRT) { #>`）。
- **`XamlOptionalChanges`/`XamlChangeId` 必须 Globalize**（`VB_CodeGenerator.cs:26-29`；类型在 `Microsoft.UI.Xaml.Settings`，`Core/KnownStrings.cs:46,161-162`）。数值 change id 用 `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`，不写 C# 转型 `(XamlChangeId)5`。
- Start lambda 用 `Sub(p)` 形态；**lambda 内不能裸 `New App()`（P0-1 实证 BC30035 / `Call New` BC30454）**，须 `Dim _application As New App()`；`()()` 双括号是委托调用笔误。
- **VB 默认入口 = STA（实证，P0-3 前置）**：VB 编译器对编译入口点（`Module`/`Class` 的 `Sub Main`）合成注入 `STAThreadAttribute`（Roslyn `SourceMethodSymbol.vb:1438-1456`；`Sub Main` 不写特性实测 `GetApartmentState()`=STA，写 `<MTAThread()>`=MTA）。故最简做法是**删除现模板 `<MTAThread()>`、不写任何特性即 STA**（与显式 `<STAThread()>` 运行时等效，显式仅作自文档化标记）；现模板 `VisualBasicAppPass1.tt:24` 的 `<MTAThread()>` 实为主动把默认 STA 覆盖成 MTA（错误方向）。`IsWin32App=false` 分支 VB 默认 STA vs C# 默认 MTA 的差异影响，P0-3 e2e（打包应用启动）确认。

### 2.2 域 1 的 DISABLE 契约（最高优先 OPEN QUESTION）

C# 契约（成对，`CSharpAppPass1.tt:22-89`）：DISABLE 时 `Program` 改名 `XamlGeneratedProgram`、生成 `XamlGeneratedMain()`（方法体内仍带 ComWrappers/XamlOptionalChanges/同步上下文/Start）、App partial 提供 `XamlGeneratedCreateApplicationInstance()` 调 `static partial void _XamlGeneratedCreateApplicationInstance()`（`:88`），partial 主体由 App pass2（`CSharpPagePass2.tt:41-44`）仅在 `hasParameterlessCtor` 时补 `new App()`。用户自定义入口 delegate 到 `XamlGeneratedProgram.XamlGeneratedMain()`（`Samples/DisableXamlGeneratedMain/Cs/Program.cs:16`）；无参构造缺失时 helper 静默空转（`CsNoCtor/Program.cs:13-24`，用户完全自写入口）。

VB 现状：DISABLE 下整个 `Program` 类不生成（`VisualBasicAppPass1.tt:21`），用户无任何启动辅助，ComWrappers/可选特性/同步上下文全裸奔。与 C# 不对等。

**约束**：`Private Shared Partial Sub` **合法**（Roslyn `SourceMethodSymbol.vb:144-213` 只拒非 Private 可访问性；`EntryPointTests.vb:780,799`、`BindingErrorTests.vb:14086,14109`；spec `type-members.md:1167`），C# `static partial void` **可逐字镜像**。DISABLE helper 必须带初始化（内联）；`New App()` 仅无参构造时输出（经 pass2 `IsApplication` 分支补内层 partial 实体，镜像 `CSharpPagePass2.tt:20-48`）；pass1 可用性由 VB partial 擦除保证。

**定稿结构**：

- 非 DISABLE：`Module Program` + `Sub Main`（内联初始化）。
- DISABLE：`Friend Module XamlGeneratedProgram` + `Sub XamlGeneratedMain`（同一段内联初始化，Start lambda 改调 `App.XamlGeneratedCreateApplicationInstance()`）。
- App partial 两层钩子：外层 `Friend Shared Sub XamlGeneratedCreateApplicationInstance()`（镜像 C# `internal static`，`CSharpAppPass1.tt:81`）→ 调内层 `Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()`（镜像 C# `private static partial`，`:88`）；内层实体由 `VisualBasicPagePass2.tt` 的 `IsApplication` 分支在 `hasParameterlessCtor` 时补 `New App()`（F3）。
- **无参构造缺失语义**：预期 VB 静默空转（与 C# 一致）——spec `type-members.md:1151`「无实现时调用被忽略」+ 代码库既有生产先例 `_AddOtherProvider`（`VisualBasicAppPass1.tt:54` 声明恒在、`VisualBasicTypeInfoPass2.tt:1052` 实现条件发射、未实现静默丢弃）。**P0-1 实证确认**后文档化。

P0-1 验收：建最小 VB DISABLE 示例验证上述结构可 vbc 编译 + 确认无参构造缺失时 VB 的实际行为（预期静默，spec `type-members.md:1151` + `_AddOtherProvider` 先例）；建 `Samples/DisableXamlGeneratedMain` VB 对应示例。

### 2.3 域 2：UnhandledException（保留不动）

现状已等价（`VisualBasicAppPass1.tt:76-83` 的 `AddHandler ... Sub(...)` 与 C# `+=` 逻辑等价，`AndAlso` 符合 VB 习惯）。正确性依赖域 1 的同步上下文联动：后台线程异常若不落在 DispatcherQueue 同步上下文，`Application.UnhandledException` handler 收不到。联动推理标「待定」，由 P0-5 端到端验证。公开面不改：生成代码不替用户吞异常。

### 2.4 域 3：DesignerGenerated

- 保留 `<DesignerGenerated()>` + `Public Sub InitializeComponent()` + `_contentLoaded` 守卫组合（`VisualBasicAppPass1.tt:40,60-64` / `VisualBasicPagePass1.tt:23,59-62`）。叠加时守卫防重复加载，机制安全。
- **如实记录实证结论**：`InitializeInstance` 机制**不存在**——Roslyn `InitializerRewriter.vb:150-158` 只对隐式默认构造器（构造器末尾）注入 `InitializeComponent`；显式 `Sub New` 漏调 `InitializeComponent` 只发 BC40054 警告（`MethodCompiler.vb:770-778`）、运行静默空白。域 3 口径 =「显式构造器策略」三选一：(a) 文档化 + 依赖 BC40054；(b) 维持现状；(c) 评估生成基类调用链。P0-2 定（可由源码直接断定，不必实测）。
- **删除 `Sub Program`**（`VisualBasicAppPass1.tt:33`）：VB 构造器恒为 `Sub New`，无引用、默认 `Public`，删除无风险。

### 2.5 域 4：WithEvents / x:FieldModifier

修 `FieldDefinition.cs` 两个构造器的 VB 槽位 + 默认槽位：

| 位置 | 现状 | 修后 VB 槽位 |
|------|------|--------------|
| 第一个构造器 `FieldDefinition.cs:49-54` | VB `() => modifier.ToTitleCase()`（`:53`）→ `internal`→`Internal` 不可编译 | `internal`→`Friend`，其余 `ToTitleCase()` |
| 第二个构造器 `FieldDefinition.cs:85` | 四语言同串 `() => modifier` → `internal`/`public` 小写不可编译 | 改走 VB 映射：`internal`→`Friend`，其余首字母大写 |
| 默认槽位 `FieldDefinition.cs:96-99` | VB `() => "private"`（小写，合法但风格不一） | `() => "Private"`（统一显式路径标题化风格，次要） |

C# 槽位（`ToLower`）不受影响；`LanguageSpecificString` 本就是四语言参数化（`LanguageSpecificString.cs:13-28`），`TypeForCodeGen`/`Language` 无回归。`Friend WithEvents` 可共存，跨程序集语义与 C# `internal` 对齐。

**WithEvents 降级分支**：唯一硬禁忌 = **值类型**（spec `type-members.md:2017` + Roslyn `SourceMemberFieldSymbol.vb:218-239`：值类型 BC30413、数组 BC30476、**接口合法**；BC30415 实为 RedimRankMismatch 与 WithEvents 无关）。降级条件 = `FieldDefinition.IsValueType`。XAML `x:Name` 字段绝大多数是 `FrameworkElement`/`DependencyObject` 派生（值类型命中很少；且上游 XamlDomValidator.cs:379-386 的 WMC0045 已拦截值类型 x:Name，降级是防御性背板）。降级方向（值类型 → 普通 `Friend`/`Private` 字段，`Handles` 不可用）已采纳；降级用户可见行为 **P0-4 实证选定 (ii) 注释**：输出普通字段 + 一行 VB 注释（`' 值类型不支持 WithEvents，已输出普通字段`）。

## 3. OPEN QUESTIONS 与归属

| 编号 | OPEN QUESTION | 归属任务 | 验收标准 |
|------|---------------|----------|----------|
| OQ1 | DISABLE 契约（已定结构，待实证落地） | P0-1 → P2-1/P2-2/P3-1 | 两层钩子结构 vbc 可编译样例 + 无参构造缺失行为实证 + 语义差异写明 |
| OQ2 | DesignerGenerated 显式构造器策略 | P0-2 → 文档/实现 | 最小页面实证 BC40054 + 选定策略 + 断言 |
| OQ3 | MTA→STA 切换影响 | P0-3 → P1-2 | e2e 启动成功 + 特性条件断言 |
| OQ4 | WithEvents 边界类型面 + 降级告警 | P0-4 → P1-1 | 类型面枚举 + 降级行为选定 + 断言 |
| OQ5 | 同步上下文跨线程异常联动 | P0-5 → P4-2 | e2e：后台异常冒泡到 UnhandledException |

## 4. 影响面与风险

- **改动文件**：`VisualBasicAppPass1.tt`（入口与 App，含 DISABLE 两层钩子）、`VisualBasicPagePass2.tt`（`IsApplication` 分支，补内层 partial 实体——**必需**）、`FieldDefinition.cs`（VB 槽位映射）、对应 `.cs`（T4 重新生成）、`Samples/DisableXamlGeneratedMain` VB 示例（按 P0-1）。
- **C# 生成器零改动**；共享模型层（`LanguageSpecificString`/`TypeForCodeGen`/`Language`）无连坐；`CompileXaml.cs:264-271` VB TaskFileManager workaround 正交。
- **行为变更**：VB 生成代码首次补入口初始化（MTA→STA、同步上下文、ComWrappers 初始化），属行为变更；现有 VB 应用在未实证前不得默认运行行为不变（P0-3 端到端实证）。
- **pass1 中间编译关键路径**：模块初始化器、STAThread、Start lambda、Friend 字段均不引入类型解析，风险低；残余风险集中在 DISABLE 契约（OQ1）与显式构造器策略（OQ2）。
- **端到端从未验证**：单元测试项目当前 broken（`docs/design-notes/xamlcompiler.md` 自述）；VB 回归项目被移除、masters 停在 UWP 命名空间。本任务补最小 VB e2e 应用（首次搭建成本，见 test-plan）。

## 5. 非目标

- 不改 C# 生成器、不改 `Language.cs` 四语言表注册、不改 `XamlCodeGenerator.cs` 派发。
- 不重建 VB 回归项目 / 不迁移 UWP 时代 masters（本任务只补最小 e2e 应用 + 单元 gate；masters 迁移是独立任务）。
- 不为 DISABLE 用户在生成代码里吞异常或替换用户事件处理。
- 不做 `DesignerGenerated` 之外的构造器注入机制创新（除非 P0-2 选 (c) 基类调用链）。
