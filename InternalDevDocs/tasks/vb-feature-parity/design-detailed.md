# 详细设计：VB 生成器特色对齐（逐文件改动）

- **状态**：plan（详细设计；P0 未决项标注「待 P0-x」）
- **权威**：`meeting-vb-feature-parity.md` RESOLUTION；逐文件锚点 `文件:行号`
- **纪律**：`.tt` 是源、`.cs` 是生成物，改完 `.tt` 用 T4 重新生成对应 `.cs`（P1-3 强制）；C# 侧零改动

---

## F1. `FieldDefinition.cs` — VB 槽位映射（`internal`→`Friend`）

**文件**：`src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/FieldDefinition.cs`

### 改动点 1.1：第一个构造器（pass2 路径）

现状（`:49-54`）：

```csharp
_fieldModifier = new LanguageSpecificString(
    () => modifier.ToLower(),       // CppCX
    () => modifier.ToLower(),       // CppWinRT
    () => modifier.ToLower(),       // CSharp
    () => modifier.ToTitleCase()    // VB —— internal → "Internal"，vbc 必错
    );
```

改为（VB 槽位 `internal`→`Friend`，其余保持标题化）：

```csharp
_fieldModifier = new LanguageSpecificString(
    () => modifier.ToLower(),
    () => modifier.ToLower(),
    () => modifier.ToLower(),
    () => modifier == "internal" ? "Friend" : modifier.ToTitleCase()
    );
```

- `ToTitleCase()` 实现见 `src/XamlCompiler/BuildTasks/Utilities/StringExtensions.cs:28-38`（只首字母大写）。
- 历史佐证：`src/XamlCompiler/TestMasters/RegressionProjects/Basic/VisualBasic/Simple/obj/x86/Debug/FieldModifierTests.g.i.vb:41` = `Friend WithEvents Internal1 As ...`。
- **期望 VB 输出**：`Friend WithEvents Button1 As Global.Microsoft.UI.Xaml.Controls.Button`；`public`→`Public`、`protected`→`Protected`、`private`→`Private` 不变。

### 改动点 1.2：第二个构造器（pass1 本地类型路径）

现状（`:85`）：

```csharp
_fieldModifier = new LanguageSpecificString(() => modifier);   // 四语言同串原样
```

该路径 `internal`→小写 `internal`、`public`→小写 `public`，VB 同样不可编译（需 `Friend`/`Public`）。改为走 VB 映射（**从四语言同串改为四参数**）：

```csharp
_fieldModifier = new LanguageSpecificString(
    () => modifier,                                    // CppCX
    () => modifier,                                    // CppWinRT
    () => modifier,                                    // CSharp —— 保持小写原样
    () => modifier == "internal" ? "Friend" : modifier.ToTitleCase()   // VB
    );
```

- 注意「其余保持标题化」只对第一个构造器成立；第二个构造器现在是原样，本改动统一两个构造器的 VB 输出。

### 改动点 1.3：默认槽位（无显式 `x:FieldModifier`）

现状（`:96-99`）：

```csharp
_fieldModifier = new LanguageSpecificString(
    () => "private",       // CppCX
    () => "protected",     // CppWinRT
    () => "private",       // CSharp
    () => "private");      // VB
```

VB 槽位 `"private"`（小写）合法但风格不统一（显式 `x:FieldModifier="private"` 走标题化输出 `Private`；master `MainPage.g.i.vb:23` 是 `private WithEvents`）。**次要**，改 VB 槽位为：

```csharp
    () => "Private");      // VB —— 统一标题化风格
```

### C# 对照基准

C# 槽位是 `modifier.ToLower()`（`FieldDefinition.cs:50-52`）与 `"private"`（`:96`），均不受影响。`CSharpPagePass1.tt:104` 输出 `fieldData.FieldModifier`（小写）保持合法。

### 验证

- 单元字符串断言：pass1/pass2 VB 输出含 `Friend WithEvents`（`internal`）、`Public WithEvents`（`public`）、`Private WithEvents`（默认/`private`）；C# 输出仍为小写 `internal`/`public`/`private`。
- 编译 gate：生成的 `.g.vb`/`.g.i.vb` 过 vbc。

### 依赖

P0-4（若 WithEvents 降级分支最终要动 `FieldDefinition` 的字段输出形态，此处同步设计；若降级仅涉及「有/无 WithEvents 修饰」，则在 `VisualBasicPagePass1.tt:98` 的输出表达式侧处理，`FieldDefinition` 只需提供判定属性）。

### 实证结果（P0-4：WithEvents 边界）

环境：dotnet SDK 10.0.400 / vbc 5.9.0-1.26379.115（`C:\Program Files\dotnet\sdk\10.0.400\Roslyn\bincore\vbc.dll`）；`net10.0`、`LangVersion latest`、`OutputType Library`；构建以 `-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false` 屏蔽仓库根 `Directory.Build.props/targets`（含 `eng\projectcaching.props` 的 MSBuildCache 链），无网络。四探针在 `C:\Users\james\Projects\vbwinui3\tmp\p0-4\`（`ProbeValueType\`、`ProbeArray\`、`ProbeInterface\`、`ProbeControl\`，各 1 个 `.vb` + `.vbproj`）。

**1. Roslyn 边界确认（值类型→BC30413、数组→BC30476、接口合法）**

`C:\Users\james\Projects\roslyn\src\Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb:218-239`（`ComputeWithEventsFieldType`）：

- `:225-227`：`If varType.IsArrayType` → 报 `ERRID.ERR_EventSourceIsArray` = **BC30476**。**任务原设想「数组→BC30415」有误**——BC30415 是 `ERR_RedimRankMismatch`（`Errors.vb:355`），与 WithEvents 无关；数组 WithEvents 的真实错误号是 **BC30476**（`Errors.vb:387`）。
- `:229-231`：`ElseIf Not (varType.IsClassOrInterfaceType OrElse (varType.Kind = SymbolKind.TypeParameter AndAlso varType.IsReferenceType))` → 报 `ERRID.ERR_WithEventsAsStruct` = **BC30413**（`Errors.vb:353`；仅显式 AsClause 时报，`:232-238`）。`IsClassOrInterfaceType` 含接口 → **接口合法**；带类约束的引用类型参数也合法。
- 错误文本（英文，`VBResources.resx`）：BC30413 = `'WithEvents' variables can only be typed as classes, interfaces or type parameters with class constraints.`（`:992`）；BC30476 = `'WithEvents' variables cannot be typed as arrays.`（`:1085`）。
- 二者均在 `ErrorFacts.IsBuildOnlyDiagnostic` 列表（`ErrorFacts.vb:296,325`），构建期必报。

vblang spec（`C:\Users\james\Projects\VBScriptDotNet\InternalDevDocs\vblang\spec\type-members.md:2017`）原文：*"It is not valid to declare an instance or shared variable as `WithEvents` if the variable is typed as a structure. In addition, `WithEvents` may not be specified in a structure, and `WithEvents` and `ReadOnly` cannot be combined."*

**2. 四探针编译结果**

| 探针 | 源码（探针文件） | vbc 结果 | 错误文本（本机中文 locale / Roslyn 英文） |
|------|------|----------|----------|
| ProbeValueType | `Public WithEvents f As Integer` + `Public WithEvents g As MyStruct` | **BC30413 ×2**（`:5,27`、`:13,27`） | `'"WithEvents" 变量只能类型化为具有类约束的类、接口或类型参数。` / `'WithEvents' variables can only be typed as classes, interfaces or type parameters with class constraints.` |
| ProbeArray | `Public WithEvents f() As Integer` | **BC30476**（`:5,27`） | `'"WithEvents" 变量不能类型化为数组。` / `'WithEvents' variables cannot be typed as arrays.` |
| ProbeInterface | `Public WithEvents f As IFoo` + `Private Sub f_Changed() Handles f.Changed` | **0 错误**（产出 `ProbeInterface.dll`） | 接口 WithEvents 合法，且 `Handles` 能绑定接口事件 |
| ProbeControl | `Public f As Integer` + `Public g As MyStruct`（无 WithEvents） | **0 错误**（产出 `ProbeControl.dll`） | 对照组：值类型普通字段合法 |

**3. 类型面枚举：x:Name 命中 `IsValueType` 的场景**

`FieldDefinition.IsValueType`（`src\XamlCompiler\BuildTasks\Microsoft\Xaml\XamlCompiler\FieldDefinition.cs:30,64`）由 `DirectUIXamlType.IsValueType`（`...\Microsoft\Xaml\DirectUI\DirectUIXamlType.cs:73-83,524-532`）决定 = `UnderlyingType.IsValueType`（`UnderlyingType==null` → false）。**关键发现：XAML 侧已有上游硬校验**——`XamlDomValidator.cs:379-386` 对带 `x:Name` 且 `duiType.IsValueType` 的元素报 **WMC0045**（`CompileError.cs:412-418`，消息 `Type '{0}', and "Value Types" in general, cannot use x:Name`，资源 `XamlCompilerResources.resx:251`）；`ValidateXaml` 有错即 `return false`（`CompileXamlInternal.cs:2656-2659`）阻断该文件代码生成；已有单测覆盖（`Tests\UnitTests\ValidatorTests.cs:421` `WMC0045_CantNameValueTypes`，`<Color x:Name>`）。`TestHelper.GenerateCodeBehind` 同样先跑校验并断言零错误（`Tests\UnitTests\TestHelper.cs:505-506`），常规代码生成测试路径也到不了降级分支。

枚举结论：

- **WinRT 结构体命名元素**（`Windows.Foundation.Point/Size/Rect`、`Windows.UI.Color`、`Windows.UI.Xaml.Thickness/CornerRadius/Duration/KeyTime/GridLength`…）：validation 阶段被 WMC0045 拦截，**到不了 `FieldDefinition`**。
- **自定义 Structure 命名元素**：类型可解析时同上被 WMC0045 拦截；pass1 未知本地类型路径（`FieldDefinition.cs:70` 第二构造器，`XamlHarvester.cs:568-574` 在 `domObject.Type.IsUnknown && IsPossiblyALocalType` 时走）**不设 `IsValueType`**（恒 false），且 `LookupIsValueType` 对 `UnderlyingType==null` 返回 false → WMC0045 不触发、IsValueType 误判为 false。若该本地类型实为结构体：降级不生效、`WithEvents` 照出 → 由 VB 编译器报 **BC30413**（清晰报错，可接受）。这是唯一现实漏点。
- **枚举 / `Nullable<T>`**：同为值类型，走 WMC0045 拦截路径。
- **结论**：XAML 元素几乎都是 FrameworkElement/DependencyObject 派生引用类型；值类型命名元素命中 `IsValueType=true` 属**极罕见**——正常路径在 validation 阶段就被 WMC0045 拦截，降级分支是**防御性背板**，服务「校验被绕过 / 旧 XAML / 本地未知类型漏点」三类边缘。

**4. 降级用户可见行为选定 = (ii) 注释**

推荐 **方案 (ii)：值类型字段输出普通字段 + 一行 VB 注释**。理由：

1. 命中率极低（见类型面枚举）→ 降级分支近乎防御性死代码，选型应最小侵入、不污染构建日志 → **排除 (iii) 警告**：生成源码无法直接产编译警告（需在编译任务侧 LogWarning 侵入日志），且 WMC0045 已用错误拦截该场景，再叠警告冗余。
2. 若真命中，用户困惑「为什么没 WithEvents」——(ii) 的注释自文档化，成本为零（VB 注释惰性、不影响编译），比 (i) 静默多一行信息，比 (iii) 不侵入日志。
3. 注释是模板侧字符串常量，可与 `xProperties` 注释先例对齐（`VisualBasicPagePass1.tt:132` `' <#=xProp.CodegenComment#>`）。

落地点（对 F1/F2 的影响）：

- **降级落在 `VisualBasicPagePass1.tt:98` 输出表达式侧**：`FieldDefinition` 已提供 `IsValueType` 判定属性（`:30`），模板按它条件输出 `WithEvents` 与否。F1 的 VB 槽位映射改动（`internal`→`Friend` 等）与降级**正交**，无需联动（对应本 F1「依赖」区注释的判断）。模板形态：
  ```tt
  <#+ foreach (FieldDefinition fieldData in Model.CodeInfo.FieldDeclarations) #>
  <#+ { #>
  <#+     if (fieldData.IsValueType) { #>
          ' 值类型不支持 WithEvents，已输出普通字段
          <#=fieldData.FieldModifier#> <#=fieldData.FieldName#> As <#=Globalize(fieldData.FieldTypeName)#>
  <#+     } else { #>
          <#=fieldData.FieldModifier#> WithEvents <#=fieldData.FieldName#> As <#=Globalize(fieldData.FieldTypeName)#>
  <#+     } #>
  <#+ } #>
  ```
  注释文本为模板常量（此处中文仅为文档示例，落地时可用英文或随模板语言调整）。
- **F2 断言**（`CodeGeneratorTests.cs` 或新增 `VbFeatureParityTests.cs`，沿用 `TestHelper.GenerateCodeBehind`）：引用类型默认字段（F1 后）断言含 `Private WithEvents btn`（**含 WithEvents**，不回归）；值类型降级断言（经 `FieldDefinition` 直构或绕过校验的路径）为 `AssertContainsString(contents, "' 值类型不支持 WithEvents，已输出普通字段")` + `Assert.IsFalse(contents.Contains("WithEvents f"))`；C# 侧不受影响（`CSharpPagePass1.tt:104` 本无 WithEvents）。
- **对逐域验收（域 4 WithEvents）**：`Friend WithEvents` / `Public WithEvents` / `Private WithEvents` 断言不变；新增「值类型降级分支按 P0-4 结论落地并断言」；C# 输出不变。

---

## F2. `VisualBasicAppPass1.tt` — 入口点重设计 + `Sub Program` 删除

**文件**：`src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicAppPass1.tt`

### 改动点 2.1：`Class Program` → `Module Program` + `Sub Main` 内联初始化 + 启动

现状（`:21-37`）：

```tt
#If Not DISABLE_XAML_GENERATED_MAIN Then
Public Class Program

    <MTAThread()> _
    <#=GeneratedCodeAttribute#>
    <#=DebuggerNonUserCodeAttribute#>
    Shared Sub Main(ByVal args() As String)
        <#=Globalize(KnownNamespaces.Xaml)#>.Application.Start(Function(p) New Global.<#=Model.CodeInfo.ClassName.FullName#>())
    End Sub

    <#=GeneratedCodeAttribute#>
    <#=DebuggerNonUserCodeAttribute#>
    Sub Program
    End Sub

End Class
#End If
```

改为（T4 时条件，**不写 `#If UsingCSWinRT Then` 伪代码**；**初始化内联于 `Sub Main` 体，弃用 `Sub New`**）：

```tt
#If Not DISABLE_XAML_GENERATED_MAIN Then
Public Module Program

    ' 不写线程模型特性：VB 编译器对编译入口点合成注入 STAThreadAttribute，默认即 STA（P0-3 实证 Roslyn SourceMethodSymbol.vb:1438-1456）；删除了旧模板的无条件 <MTAThread()>（主动把默认 STA 覆盖成 MTA，错误方向）
    <#=GeneratedCodeAttribute#>
    <#=DebuggerNonUserCodeAttribute#>
    Sub Main(ByVal args() As String)   ' Module 成员隐式 Shared，不写 Shared
<#  if (ProjectInfo.UsingCSWinRT) { #>
        Global.WinRT.ComWrappersSupport.InitializeComWrappers()
<#  } #>
<#  foreach (var change in ProjectInfo.EnabledXamlOptionalChanges) { #>
        <#=Globalize(KnownTypes.XamlOptionalChanges)#>.EnableChange(<#= int.TryParse(change, out _) ? $"CType({change}, {Globalize(KnownTypes.XamlChangeId)})" : $"{Globalize(KnownTypes.XamlChangeId)}.{change}" #>)
<#  } #>
<#  foreach (var change in ProjectInfo.DisabledXamlOptionalChanges) { #>
        <#=Globalize(KnownTypes.XamlOptionalChanges)#>.DisableChange(<#= int.TryParse(change, out _) ? $"CType({change}, {Globalize(KnownTypes.XamlChangeId)})" : $"{Globalize(KnownTypes.XamlChangeId)}.{change}" #>)
<#  } #>
        <#=Globalize(KnownNamespaces.Xaml)#>.Application.Start(
            Sub(p)
<#  if (ProjectInfo.UsingCSWinRT) { #>
                Dim context As New Global.Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Global.Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread())
                Global.System.Threading.SynchronizationContext.SetSynchronizationContext(context)
<#  } #>
                Dim _application As New Global.<#=Model.CodeInfo.ClassName.FullName#>()
            End Sub)
    End Sub

End Module
#End If
```

**关键语义**（均为 RESOLUTION 硬性裁决）：

- **Module 内 `Sub Main` 不写 `Shared`**（BC30433 实测；模块成员隐式 Shared）。
- **初始化内联于 `Sub Main` 方法体**（ComWrappers / XamlOptionalChanges 直接写在 `Application.Start` 之前），对齐 C# `CSharpAppPass1.tt:30-67`；**不再由 `Sub New` 承担**（`Sub New` + `#If Not DISABLE` 的归属割裂会让 DISABLE 用户裸奔）。DISABLE 分支（`XamlGeneratedMain`）复用同一段初始化（见改动点 2.3）。
- `UsingCSWinRT`/`IsWin32App` 走 **T4 时条件**（`<# if (ProjectInfo.UsingCSWinRT) { #>`，对齐 `CSharpAppPass1.tt:39`）。
- `XamlOptionalChanges`/`XamlChangeId` 走 `Globalize`（`VB_CodeGenerator.cs:26-29`）；数值 change id 用 `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`；类型全名在 `Core/KnownStrings.cs:161-162`（`Microsoft.UI.Xaml.Settings.XamlOptionalChanges` / `...XamlChangeId`）。
- 同步上下文设置仅在 `UsingCSWinRT` 分支输出（对齐 C# `CSharpAppPass1.tt:48-57`）。
- **`Sub(p)` lambda 内不能裸 `New App()`（P0-1 实证：裸 `New X()` 语句 → BC30035、`Call New X()` → BC30454）**：Start lambda 末尾必须 `Dim _application As New Global.<FullName>()`。`VisualBasicAppPass1.tt:28` 的 `Function(p) New App()` 合法仅因它是单表达式 Function lambda 的返回值，不是语句；`()()` 双括号是委托调用笔误。

### 实证结果（P0-3 前置：VB 入口默认线程模型）

环境：dotnet SDK 10.0.400 / vbc 5.9.0（`C:\Program Files\dotnet\sdk\10.0.400\Roslyn\bincore\vbc.dll`）；`net10.0`、`LangVersion latest`、`OutputType Exe`、`ImplicitUsings disable`；构建以 `-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false` 屏蔽仓库根 `Directory.Build.props/targets`（含 `eng\projectcaching.props` 的 MSBuildCache 链），无网络。三个工程各 2 个文件：`Program.vb`（`Module Program` + `Sub Main`，体为 `System.Console.WriteLine(System.Threading.Thread.CurrentThread.GetApartmentState())`）+ `p0-3.vbproj`；运行 `bin\Debug\net10.0\p0-3.exe`，打印后立即退出，退出码 0。

| 探针（`tmp\p0-3-probe\`） | Sub Main 特性 | `GetApartmentState()` 输出 | 结论 |
|---|---|---|---|
| Probe1_NoAttr | 无任何线程模型特性 | `0` = `ApartmentState.STA`（STA=0 / MTA=1 / Unknown=2） | **VB 默认入口 = STA，实锤** |
| Probe2_MTA | `<System.MTAThreadAttribute()>`（现模板 `VisualBasicAppPass1.tt:24` 写法） | `1` = `MTA` | 显式 MTA 生效，覆盖默认 |
| Probe3_STA | `<System.STAThreadAttribute()>`（设计目标写法） | `0` = `STA` | 显式 STA 生效，与默认一致 |

**Roslyn 源码锚点**（`C:\Users\james\Projects\roslyn`，只读）：

- **注入实现**：`src\Compilers\VisualBasic\Portable\Symbols\Source\SourceMethodSymbol.vb:1438-1456`，`AddSynthesizedAttributes` 覆写——`compilation.GetEntryPoint()` 返回的入口方法、且该方法**未显式写 STAThread/MTAThread** 时，`AddSynthesizedAttribute(attributes, compilation.TrySynthesizeAttribute(WellKnownMember.System_STAThreadAttribute__ctor))` 合成注入 `System.STAThreadAttribute`。注释原文（`:1441-1443`）："Emit synthesized STAThreadAttribute for this method if both the following requirements are met: (a) This is the entry point method. (b) There is no applied STAThread or MTAThread attribute on this method."
- **抑制条件**：`SourceMethodSymbol.vb:2014-2022` `HasSTAThreadOrMTAThreadAttribute`——只要显式写 STA 或 MTA 之一，合成即跳过；`:1740-1741` 解码显式 STAThread 特性。
- **入口点门控（非方法名门控）**：测试 `src\Compilers\VisualBasic\Test\Emit\Attributes\AttributeTests_WellKnownAttributes.vb:5757-5785`（`TestSynthesizedSTAThread`：`Module Module1` + `Sub Main` 编为 Exe → Main 有合成 STAThread、`goo` 没有）；`:5788-5812`（`TestNoSynthesizedSTAThread_01`：同一源码编为 Dll → Main 无 STAThread）——合成只跟「是编译入口点」绑定，与是否叫 `Main` 无关。
- **C# 对照**：`src\Compilers\CSharp\` 全库无 STAThread 合成逻辑（仅测试 `Test\Symbol\Symbols\MissingSpecialMember.cs:843` 引用 `WellKnownType.System_STAThreadAttribute`）→ C# `Main` 不写 `[STAThread]` 默认 MTA。

**对设计的影响**：

1. **VB `Sub Main` 不写特性时默认线程模型 = STA**（实证 Probe1 + Roslyn `SourceMethodSymbol.vb:1438-1456`）。
2. **现模板 `VisualBasicAppPass1.tt:24` 无条件 `<MTAThread()>` 的性质 = 主动覆盖默认 STA → MTA**（错误方向；WinUI3 需 STA UI 线程，此方向相反）。设计文档先前「MTA→STA 属行为变更」的表述需修正（见第 4 点）。
3. **最简做法 = 删除 `<MTAThread()`、不写任何特性**：VB 默认 STA 自动生效（实证 Probe1），与设计目标 `<STAThread()>` 运行时结果完全一致（均为 STA）。显式 `<STAThread()>` 行为冗余、非必需；保留仅作「自文档化标记 + 与 C# 模板 `[STAThread]`（`CSharpAppPass1.tt:31-33`）形态对齐」的取舍。两者皆可：P0-3 建议按「不写特性（最简）」验收；若保留显式 `<STAThread()>`，e2e 行为与「不写」等效。
   - **`IsWin32App=false`（打包/MSIX）分支**：VB 不写特性默认 STA；C# 该分支不写 `[STAThread]` 默认 MTA（`CSharpAppPass1.tt:31` 仅 `IsWin32App` 时输出，即 `EnableWin32Codegen`，`CompileXamlInternal.cs:1913`）。两者确有差异，但方向判断：STA 是 WinUI3 UI 线程所需方向，VB 侧默认 STA 与 IsWin32App=true 的 C# 路径同值，不会比 C# 更差；C# 打包分支用 MTA 是微软既有取舍（该分支是否真的以 MTA 主线程跑 XAML，超出本探针范围）。判断：**中等置信度（设计推断，e2e 由 P0-3 打包应用验收确认）——VB 打包分支默认 STA 对启动无实际负面影响，反而更贴近 WinUI3 需求**。
4. **设计文档表述修正**：「MTA→STA 切换属行为变更」（README P0-3 OQ3 / design-overview 2.1）应改写为：**现模板 `<MTAThread()>` 是主动把 VB 语言默认 STA 覆盖成 MTA；改动是「移除错误的 MTA 覆盖、让 VB 默认 STA 生效」，而非「新注入 STA 行为」**。对既有按旧模板编译的 VB 应用，输出线程模型确实由 MTA 变为 STA，**仍属行为变更**（此点保留），但方向表述应从「新增特性」改为「撤销错误覆盖、回归语言默认」。

**对 P0-3 e2e 验收的影响**：e2e 需额外确认打包（IsWin32App=false）分支 VB 默认 STA 下 WinUI3 应用正常启动（与 C# 打包分支 MTA 对照）；若启动成功即同时证明「VB 打包分支默认 STA 无碍」。IsWin32App=true 分支 STA 启动成功已由 Probe3 证明单线程模型可行，e2e 主要验证 WinUI3 全栈（Application.Start + XAML 装载）在该线程模型下工作。

### 实证结果（P0-3 e2e：最小 VB WinUI3 应用）

环境：dotnet SDK 10.0.400 / Windows 11（26200）；**全离线**（`tmp/p0-3/NuGet.config` 清空 packageSources + `tmp/p0-3/Directory.Build.props/targets` 空壳屏蔽仓库根 MSBuild 链；全局缓存 `C:\Users\james\.nuget\packages\` 命中，零网络）；**未安装 Windows App Runtime MSIX**（`C:\Program Files\WindowsApps` 无 runtime 包、注册表无 `Microsoft\Windows App SDK` 键），故用 `WindowsAppSDKSelfContained=true`（WinUI 原生 runtime 从缓存各子包 `runtimes-framework\win-x64\native` 拷入输出目录）。

**工程结构**（`tmp/p0-3/`）：

| 文件 | 内容 |
|------|------|
| `NuGet.config` | packageSources 清空（离线还原） |
| `Directory.Build.props` / `.targets` | 空壳（屏蔽仓库根 MSBuild 链，同 p0-1 做法） |
| `E2e/E2e.vbproj` | 见下 |
| `E2e/Program.vb` | `Partial Module Program` + `Sub Main(ByVal args() As String)`，**不写任何线程模型特性**；开头 `File.WriteAllText("...\tmp\p0-3\sta.log", Thread.CurrentThread.GetApartmentState())`；然后 `ComWrappersSupport.InitializeComWrappers()` + `Application.Start(Sub(p) ... Dim _application As New App())`（裸 `New App()` 语句 → BC30035，P0-1 已实证）；`Try/Catch` 写 `startup-error.log` 后 `Throw` |
| `E2e/App.xaml` | `<Application x:Class="E2e.App" ...>` + `<Application.Resources>`（含 `XamlControlsResources`） |
| `E2e/App.xaml.vb` | `Namespace Global.E2e` + `Partial Public Class App : Inherits Application`；`OnLaunched` 建 `MainWindow`、`Activate()`、起 `DispatcherQueueTimer`（4s）`Tick → Environment.Exit(0)` 自动退出 |
| `E2e/MainWindow.xaml` / `.xaml.vb` | `x:Class="E2e.MainWindow"`，Content 一个 TextBlock |
| `E2e/app.manifest` | dpiAwareness PerMonitorV2 + supportedOS |

`Program.vb` 用 `Partial Module Program`（非普通 `Module Program`）：nukepayload2 支持包注入同名的 `Partial Module Program` 模块初始化器（`Sub New()` 调 `WindowsAppRuntime_EnsureIsLoaded` 加载自包含 runtime），VB 要求所有 partial 声明都带 `Partial`（否则 BC30102）。

**选型与踩坑**（4 轮迭代）：

1. **TFM/WindowsSdkPackageVersion**：`net8.0-windows10.0.19041.0`。SDK 对该 TFM 默认 `WindowsSdkPackageVersion=10.0.19041.57`（`Microsoft.NETCoreSdk.BundledVersions.props`），但缓存只有 `microsoft.windows.sdk.net.ref\10.0.26100.38` → 显式 `<WindowsSdkPackageVersion>10.0.26100.38</WindowsSdkPackageVersion>`。`.38` 是 `DotnetPlatform` 类型包，**不能 PackageReference**（NU1213），必须 FrameworkReference（隐式 SDK 机制对 Revision>34 的 26100 包不自动挂，故显式 `<FrameworkReference Include="Microsoft.Windows.SDK.NET.Ref" />`；构建期日志确认 `Selected targeting pack 'Microsoft.Windows.SDK.NET.Ref@10.0.26100.38'`，提供 `WinRT.Runtime.dll`/`Microsoft.Windows.SDK.NET.dll`）。
2. **Page 项**：显式 `<Page Include="App.xaml"/>` 与 SDK/WinUI 隐式 `Page` 项重复 → NETSDK1022；删除显式项即可。
3. **VB 命名空间合并陷阱（关键）**：XAML 编译器生成 partial 用 `Namespace Global.E2e`（根级 `E2e`）；VB 会把 `RootNamespace`（E2e）**前插**到普通 `Namespace E2e` → `E2e.E2e`，两者不合并 → 用户侧 `Partial Class App` 缺 `InitializeComponent` → BC30451 → `XamlPreCompile` 的 vbc 失败 → 无 LocalAssembly → XAML pass2 报 WMC1509 + WMC9999（NullReferenceException，`GeneratedCodeFiles` 空）。**修复：code-behind 与生成代码同写 `Namespace Global.E2e`**（探针 `tmp/p0-3/nsprobe/` 复现：`Namespace Global.E2e` 产 `E2e.ClassB`，`Namespace E2e` 产 `E2e.E2e.ClassA`——RootNamespace 前插是 VB 与 C# 的根本差异，C# 生成 `namespace E2e` 无此问题）。这是**独立的库存编译器 VB bug**（超出 F1-F6 范围），e2e 以工作区规避；repo 自己的 XamlCompiler 若沿用 `Namespace Global.X` 则用户侧须同写（文档化提醒，暂不改生成器）。
4. **自包含部署**：`WindowsAppSDKSelfContained=true` + `Platform=x64` + `RuntimeIdentifier=win-x64`；原生 runtime（`Microsoft.ui.xaml.dll`/`Microsoft.WindowsAppRuntime.dll`/`CoreMessagingXP.dll`/`DWriteCore.dll`/`.mui` 资源等）从缓存各子包 `runtimes-framework\win-x64\native` 全量拷入输出目录。

PackageReference：`Microsoft.WindowsAppSDK` 2.2.0（元包，其全部子包 `Base/Foundation/InteractiveExperiences/WinUI/DWrite/Widgets/AI/ML/Runtime` 及 `Microsoft.Web.WebView2` 均在缓存、离线解析成功）+ `Nukepayload2.UI.VBWinUI3` 0.9.4-beta（定义 `DISABLE_XAML_GENERATED_MAIN`、`XamlPreCompile` 设 `VBRuntime=None`、注入自包含 VB initializer）。

**构建结果**：`dotnet restore` 退出码 0（全离线，缓存命中）；`dotnet build` 退出码 0，**0 错误**，6 警告（5× CA1416 平台分析 + 1× BC40005 生成 `XamlTypeInfo.g.vb` 的 `BoxedType` 隐藏基类方法，均无害）。生成的 `App.g.i.vb` 仍含库存模板的 `<MTAThread()>` + `Class Program`（`Shared Sub Main` + `Function(p) New App()`），但被 `#If Not DISABLE_XAML_GENERATED_MAIN` 包住，nukepayload2 已定义该常量 → 整块跳过；手写 `Module Program` 是唯一入口点。

**启动结果**：`bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\E2e.exe` 后台启动，2s 时进程存活（窗口已建、消息泵在跑），约 4s 后 DispatcherQueueTimer 触发 `Environment.Exit(0)` 自动退出，**退出码 0**（无 `timeout` 击杀、无 `startup-error.log`）；另一次 `timeout 20` 前台运行同样退出码 0。**sta.log 内容 = `STA`**（单行，无换行）。

**一句结论**：不写线程模型特性的 VB 入口（手写 `Module Program + Sub Main`）在真实 WinUI3 应用（库存 Microsoft.UI.Xaml.Markup.Compiler 3.0.0.2606 + WinAppSDK 2.2.0，自包含部署）**可构建、可启动、窗口正常、自动退出码 0，入口线程 `GetApartmentState()`=STA**——实证「删除 `<MTAThread()`、不写特性」即为可用且线程模型正确的 VB WinUI3 入口形态，同时构成 L3 回归资产（`tmp/p0-3/E2e/`）。

### 实证结果（P0-5：同步上下文跨线程异常联动）

环境：同 P0-3 e2e（dotnet SDK 10.0.400；Windows 11 26200；全离线 `tmp/p0-5/NuGet.config` 清空源 + 空壳 `Directory.Build.props/targets`；WinAppSDK 2.2.0 自包含）。工程 = **复制 `tmp/p0-3/E2e/` → `tmp/p0-5/E2e/`**（P0-3 资产原样保留），在原结构上加：App 构造器注册 `AddHandler Me.UnhandledException`（记 `tmp/p0-5/unhandled.log` + `args.Handled=True` + 300ms 延迟 `Environment.Exit(42)`）；`--case a/b/c/d` 选择触发方式；10s 超时 `Environment.Exit(43)`；`--nosync` 控制（入口不设 DispatcherQueueSynchronizationContext）。构建 0 错误（同 P0-3 的 CA1416/BC40005 无害警告）。

**各轮结果**（每轮独立进程；退出码 = 进程真实退出码，经 cmd 批处理捕获）：

| 轮 | 触发方式 | handler 触发？ | 退出码 | 日志证据（`tmp/p0-5/`） |
|----|----------|---------------|--------|--------------------------|
| a | `Task.Run(Sub() Throw New Exception("BG_CASE_A_RAW_TASK"))`，不 await、不 post UI（unobserved） | **否** | 43（10s 超时） | `result-a.log`：`CASE_A task scheduled` + `TIMEOUT handlerFired=False`；`unhandled.log` 无 |
| b | 后台 `Task.Run` 内 `DispatcherQueue.TryEnqueue(Sub() Throw ...)` 把抛异常委托调度到 UI 线程 | **否** | **-1073741189 = 0xC000027B（STATUS_STOWED_EXCEPTION，进程崩溃）** | `result-b.log`：`TryEnqueue returned True` 后进程即亡，无 TIMEOUT、无 HANDLER_FIRED |
| c | `Async Sub`：`Await Task.Delay(300)` 后 `Await Task.Run(Sub() Throw ...)`（同步上下文存在，await 恢复回 UI 线程） | **是** | **42（handler 触发自动退出）** | `result-c.log`：`CASE_C resumed on uiThreadId=1`；`unhandled.log`：`HANDLER_FIRED case=c threadId=1 ex=System.Exception: BG_CASE_C_ASYNC_AWAIT` |
| c --nosync | 同上 async 场景，但入口**不设** DispatcherQueueSynchronizationContext | **否** | **-532462766 = 0xE0434352（.NET 未处理异常，崩溃）** | `result-c-nosync.log`：`CASE_C resumed on uiThreadId=5`（线程池线程）；stdout 堆栈 `Task.<>c.<ThrowAsync>`（async void 异常交付） |
| d（控制） | 纯 UI 线程 `DispatcherQueueTimer.Tick` 直接 `Throw`（无后台线程） | **否** | **-1073741189 = 0xC000027B（崩溃）** | `result-d.log`：`CASE_D timer tick firing on uiThreadId=1` 后即亡 |

**必要条件结论**：后台线程异常要**可靠冒泡到 `Application.UnhandledException`**，需要**同时**满足：

1. **入口设了 DispatcherQueueSynchronizationContext**（P0-3 修正后的入口已具备；`--nosync` 反例实证：无它时 async void 续体在**线程池线程**（id 5）恢复、异常在线程池线程重抛 → 0xE0434352 崩溃，handler 收不到）；
2. **异常经 async/await 状态机异常传播路径**送达 UI 线程（`Await Task.Run(faulted)` 续体恢复回 UI 线程后重抛 → XAML 框架路由到 handler）。

**不满足则收不到**：纯 `Task.Run` unobserved（a）→ .NET Core 3+ 未观察 Task 异常被静默吞掉（不崩溃、不触发）；`DispatcherQueue.TryEnqueue`/`DispatcherQueueTimer` 回调内直抛（b/d）→ 无论是否后台线程发起，**都不路由到 `Application.UnhandledException`**，而是以 `STATUS_STOWED_EXCEPTION`（0xC000027B）崩溃（该版本 WinAppSDK 投影无 `DispatcherQueue.UnhandledException` 事件可接管，代码里引用即编译期 BC30456 证伪）。即：**「DispatcherQueue 调度」本身不是充分条件，反而是崩溃路径**；可靠路径是「同步上下文 + async/await 交付」。

**对 test-plan E3 断言的具体化**（已回写 `test-plan.md` E3 行）：

- e2e 触发后台异常**必须用 async/await 恢复路径**（case c：`Async Sub` + `Await Task.Run(Sub() Throw ...)`，入口保留 DispatcherQueueSynchronizationContext）→ 断言 `unhandled.log` 出现 `HANDLER_FIRED`、进程退出码 42。
- **显式断言负例**（钉死必要条件）：纯 `Task.Run` unobserved → 不触发、不崩溃（10s 超时退出 43）；`DispatcherQueue.TryEnqueue` 直抛 → 不触发且进程崩溃（0xC000027B）——E3 不得用 DispatcherQueue 直抛来播种后台异常。
- 「UI 线程未被后台异常击穿」细化：仅 async/await 交付路径能保持 UI 线程存活；DispatcherQueue 直抛会击穿进程（崩溃），因此真实应用把后台异常送 UI 必须走 await 或自带 DispatcherQueue 级错误接管。
- 附加控制：`--nosync` 反例应保留为回归断言（无同步上下文 → 0xE0434352 崩溃、不触发）。

### 改动点 2.2：删除 `Sub Program`

删除 `:33` 的 `Sub Program / End Sub`（VB 构造器恒为 `Sub New`，无引用、默认 `Public`，删除无风险；C# 老登实测 `Class Program` 内 `Sub Program` 为可编译死方法）。

### 改动点 2.3：DISABLE 分支（镜像 C# `static partial void`）

当前 `#If Not DISABLE_XAML_GENERATED_MAIN` 包住整个入口类 → DISABLE 下 VB 无任何启动辅助。`Private Shared Partial Sub` 合法（Roslyn `SourceMethodSymbol.vb:144-213` 只拒非 Private；`EntryPointTests.vb:780,799`、`BindingErrorTests.vb:14086,14109`；spec `type-members.md:1167`），C# `static partial void` 可**逐字镜像**，pass2 整体输出回退预案不再需要。

在改动点 2.1 的 `#If Not DISABLE...`（`Module Program`）块**旁并列新增** `#If DISABLE...` 块输出 `Friend Module XamlGeneratedProgram`（**与 `Program` 共享同一段内联初始化**，`Sub XamlGeneratedMain` 体与改动点 2.1 的 `Sub Main` 相同；Start lambda 内改调 `Global.<AppFullName>.XamlGeneratedCreateApplicationInstance()`）；App partial 内（`:40` 的 `Partial Class <App>` 块内）补**两层钩子**：

  ```vb
  #If DISABLE_XAML_GENERATED_MAIN Then
      <#=GeneratedCodeAttribute#>
      <#=DebuggerNonUserCodeAttribute#>
      Friend Shared Sub XamlGeneratedCreateApplicationInstance()   ' 外层：镜像 C# internal static（CSharpAppPass1.tt:81）
          _XamlGeneratedCreateApplicationInstance()
      End Sub
      Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()   ' 内层：镜像 C# private static partial（:88）
      End Sub
  #End If
  ```

内层 partial 的实体（`New App()`）由 `VisualBasicPagePass2.tt` 的 `IsApplication` 分支在 `hasParameterlessCtor` 时补全（见 F3，镜像 `CSharpPagePass2.tt:20-48`）。**无参构造缺失语义**：预期 VB 静默空转（与 C# 一致；spec `type-members.md:1151`「无实现时调用被忽略」+ `_AddOtherProvider` 先例 `VisualBasicTypeInfoPass2.tt:1052` 条件发射），**P0-1 实证确认**后文档化。

### 实证结果（P0-1，dotnet 10.0.400）

环境：dotnet SDK 10.0.400 / vbc 5.9.0（`C:\Program Files\dotnet\sdk\10.0.400\Roslyn\bincore\vbc.dll`）；`net10.0`、`LangVersion latest`、`OutputType Exe`；全离线（`C:\Users\james\Projects\vbwinui3\tmp\p0-1\` 内置空 `Directory.Build.props/targets` 屏蔽仓库根 MSBuild 链 + 清空 NuGet 源，零网络）。每个工程 4 个文件：`App.vb`（声明部分）、`AppImpl.vb`（实现部分/缺）、`Program.vb`（`Module Program` + `Sub Main` 调外层钩子）、`p0-1.vbproj`。

**1. 工程 A（WithCtor，设计原样）——BC30035 语法错误（与预期不符，根因已定位）**
- 文件：`C:\Users\james\Projects\vbwinui3\tmp\p0-1\WithCtor\`。声明部分 `Friend Shared Sub XamlGeneratedCreateApplicationInstance()` 调内层 + `Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()`（空体，带 Partial）；实现部分（AppImpl.vb）按 F3 原样写裸 `New App()`。
- 实测：`dotnet build --nologo -v q` → `AppImpl.vb(9,9): error BC30035: 语法错误`（退出码 1）。
- 根因（与 partial 机制无关）：**VB 不允许裸 `New X()` 作为语句**。隔离探针 `C:\Users\james\Projects\vbwinui3\tmp\p0-1\Probe\`（无任何 partial）同样报 `Probe.vb(11,9): error BC30035`；改 `Call New Probe()` 亦不行（`Probe.vb(9,14): error BC30454: 表达式不是方法`）。C# 的 `new App();` 不能逐字镜像成 VB 裸语句。
- 修正后（`C:\Users\james\Projects\vbwinui3\tmp\p0-1\WithCtorFixed\`，实现体改 `Dim _application As New App()`，声明/实现结构不变）：编译成功，0 警告 0 错误 → **两层钩子（声明带 Partial 空体 + 实现不带 Partial）结构本身合法**。
- 运行验证（`C:\Users\james\Projects\vbwinui3\tmp\p0-1\RunProbe\`，App 构造器打印 `APP_CTOR_RAN`）：`dotnet build` 后直接执行 `bin\Debug\net10.0\p0-1.exe`，stdout 输出 `APP_CTOR_RAN`、`HOOK_RAN`，退出码 0 → 实现部分被合并且真实执行（非仅编译通过）。

**2. 工程 B（NoCtor）——编译成功 0 错误 = 静默（符合预期）**
- 文件：`C:\Users\james\Projects\vbwinui3\tmp\p0-1\NoCtor\`。App **无任何可访问性无参构造**（仅 `Public Sub New(ByVal name As String)`），声明部分在、实现部分缺（模拟 hasParameterlessCtor=false 时 pass2 不输出；AppImpl.vb 只有空 `Partial Class App ... End Class`）。
- 实测：`dotnet build` → 成功，0 警告 0 错误。
- 为什么零错误即证明「调用被忽略 = 静默」：反例 `C:\Users\james\Projects\vbwinui3\tmp\p0-1\NoCtorWithImpl\`（App 同样无无参构造，但实现部分存在、体为 `Dim _application As New App()`）实测报 `AppImpl.vb(6,33): error BC30455: 没有为"Public Sub New(name As String)"的形参"name"指定实参` → 只要实现部分存在而 App 无无参构造，构造调用必然编译错误。故工程 B 零错误只能解释为：实现缺 → 对未实现 partial 方法的调用被编译器静默移除（无构造调用残留）→ 无错误。与 C# 一致。
- 对任务原设想的修正：仅留 `Private Sub New()` 并不构成 hasParameterlessCtor=false —— F3 反射用 `BindingFlags.Public | NonPublic | Instance` + `EmptyTypes`，`Private Sub New()` 也算「有无参构造」；实测含 `Private Sub New()` + 实现 `Dim _application As New App()` 的工程（NoCtorWithImpl 初版）编译成功（实现是 App 类成员，同类内可访问私有构造）。真正的 hasParameterlessCtor=false 必须连私有无参构造都不存在，F3 反射写法本身正确。

**3. 工程 C（WithPartialImpl）——实现部分带 Partial：BC31433 错误**
- 文件：`C:\Users\james\Projects\vbwinui3\tmp\p0-1\WithPartialImpl\`（设计原样，先被 `New App()` 的 BC30035 掩盖）。修正体后（`C:\Users\james\Projects\vbwinui3\tmp\p0-1\WithPartialImplFixed\`，体 `Dim _application As New App()`，实现部分保留 `Partial`）：
- 实测：`AppImpl.vb(5,32): error BC31433: 方法"_XamlGeneratedCreateApplicationInstance"不能声明为"Partial"，因为只有一个方法"_XamlGeneratedCreateApplicationInstance"可以标记为"Partial"` → **实现部分必须不带 Partial**。代码库先例 `VisualBasicTypeInfoPass2.tt:1052`（实现不带 Partial）是正确且必需的，并非「两者择一」。

**对 F3 的关键修正（P0-1 新发现）**：实现体 `New <#=Model.CodeInfo.ClassName.ShortName#>()` 需改为 `Dim _application As New <#=Model.CodeInfo.ClassName.ShortName#>()`（或等价 Dim 语句）；裸 `New X()` 在 VB 中不是合法语句（BC30035），`Call New X()` 也不是（BC30454）。`VisualBasicAppPass1.tt:28` 的 `New Global.<FullName>()` 之所以合法，是因为它是 `Function(p) ...` lambda 体的末表达式（返回值），非裸语句。

**一句结论**：DISABLE 两层钩子 VB 结构（外层 `Friend Shared Sub XamlGeneratedCreateApplicationInstance` + 内层声明 `Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()` 空体，实现 `Private Shared Sub _XamlGeneratedCreateApplicationInstance()` 不带 Partial、体为 Dim 语句）落地可行；无参构造缺失（真正无任何可访问性无参构造）= 静默空转，与 C# 一致，P0-1 实证确认。

### 实证结果（P0-2，dotnet 10.0.400）

环境：dotnet SDK 10.0.400 / vbc 5.9.0-1.26379.115（`C:\Program Files\dotnet\sdk\10.0.400\Roslyn\bincore\vbc.dll`）；`net10.0`、`LangVersion latest`、`OutputType Exe`、`ImplicitUsings disable`；全离线（`C:\Users\james\Projects\vbwinui3\tmp\p0-2\` 内置空 `Directory.Build.props/targets` 屏蔽仓库根 MSBuild 链 + 清空 NuGet 源，零网络）。三场景各 3 个文件：`Page.vb`（`<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>` 特性 + `Partial Class Page` + `Private _contentLoaded As Boolean` + `Public Sub InitializeComponent()`（体：`If _contentLoaded Then Return` + `System.Console.WriteLine("INIT")` + `_contentLoaded = True`）+ `Public Sub Foo()` 打印 "FOO"）、`Program.vb`（`Module Program` + `Sub Main` 内 `Dim p As New Page()` + `p.Foo()` + `System.Console.ReadLine()` 保持进程）、`p0-2.vbproj`。运行 exe 时 stdin 重定向 `< /dev/null` 使 `ReadLine` 立即返回，退出码均为 0。

**1. 编译结果**（`dotnet build --nologo -v q`，三场景退出码均为 0）

| 场景 | 目录 | 编译结果 |
|------|------|----------|
| A 显式 `Sub New` 漏调 `InitializeComponent` | `tmp/p0-2/ExplicitMissing/` | **1 警告 0 错误：BC40054 复现** |
| B 隐式默认构造器（不写 `Sub New`） | `tmp/p0-2/ImplicitCtor/` | 0 警告 0 错误 |
| C 显式 `Sub New` 调 `InitializeComponent`（对照） | `tmp/p0-2/ExplicitCall/` | 0 警告 0 错误 |

BC40054 全警告文本（中文 locale；定位 `Page.vb(17,16)` = 显式 `Public Sub New()` 声明处；`WRN_ExpectedInitComponentCall2 = 40054` 见 Roslyn `Errors.vb:1845`）：

```
Page.vb(17,16): warning BC40054: '设计器生成的类型"Page"中的"Public Sub New()"应调用 InitializeComponent 方法。
```

**2. 运行结果**（`bin\Debug\net10.0\p0-2.exe`）

| 场景 | stdout | 实证结论 |
|------|--------|----------|
| A 显式漏调 | 仅 `FOO`，**无 `INIT`** | 运行静默空白：页面可用（`FOO` 打印）但控件未初始化（`INIT` 未打印） |
| B 隐式构造器 | `INIT` + `FOO` | Roslyn `InitializerRewriter.vb:150-158` 在**隐式默认构造器**末尾注入 `InitializeComponent` 调用（门控条件 `constructorMethod.IsImplicitlyDeclared` `:150`）→ 控件已初始化 |
| C 显式调用 | `INIT` + `FOO` | 显式 `Sub New` 体内首行 `InitializeComponent()` → 控件已初始化（对照组，0 警告） |

**3. 策略推荐：（a）文档化 + 依赖 BC40054 警告**

- 机制已用本地 vbc 实测钉死（Roslyn 源码佐证只读核实）：`GetDesignerInitializeComponentMethod`（`MethodCompiler.vb:864-879`）仅需「类带 `DesignerGeneratedAttribute` + 存在匹配的 `InitializeComponent` Sub」即识别；`InitializerRewriter.vb:150` 只在 `constructorMethod.IsImplicitlyDeclared`（编译器隐式构造器）时注入调用；`MethodCompiler.vb:761-781` 对**任何未调 InitializeComponent 的显式构造器**报 BC40054。即：**警告是编译器级机制，生成代码无法强制**（VB 无 C# 部分类构造器强制注入机制；生成代码对用户显式 `Sub New` 无控制权）。
- 为什么否决 (b) 维持现状：维持现状 = 显式 `Sub New` 用户漏调时仅 1 条警告（BC40054）+ 运行静默空白，无文档引导，用户易踩坑；不增加任何成本即可文档化。
- 为什么否决 (c) 生成基类调用链：过度设计且结构性不可行——InitializeComponent 是用户 partial 类自身成员（引用同类的具名控件字段），基类构造器无法调用派生类型的方法实现；WinUI 根类型（`Microsoft.UI.Xaml.Controls.Page` / `Application`）是框架类型不可改；若另插中间基类则侵入用户类型层次（用户未要求的继承）+ 与 Roslyn 隐式注入及 `_contentLoaded` 守卫冲突（双重初始化风险）。否决。
- **对 P2-3 落地的影响**：选 (a) → 生成代码**不额外输出**（`VisualBasicAppPass1.tt`/`VisualBasicPagePass1.tt` 的 `DesignerGenerated()` + `InitializeComponent` + `_contentLoaded` 组合保留现状，不新增基类/注入），落地动作 = 文档注明「显式 `Sub New` 必须调 `InitializeComponent`，漏调编译器发 BC40054 且运行静默空白」；F6 断言层加「生成代码含 `InitializeComponent` 调用 + `_contentLoaded` 守卫 + `DesignerGenerated()` 特性」并断言「生成代码不额外输出构造器注入/基类调用链」。

**一句结论**：显式构造器策略选定 = (a) 文档化 + 依赖 BC40054 警告；隐式构造器由 Roslyn `InitializerRewriter` 自动注入 `InitializeComponent`（B 实测），显式构造器由编译器 BC40054 警示 + 文档约束，生成代码不额外输出。

### C# 对照基准

`CSharpAppPass1.tt:22-89`：

```csharp
#if !DISABLE_XAML_GENERATED_MAIN
    public static class Program
#else
    internal static class XamlGeneratedProgram
#endif
    {
#if !DISABLE_XAML_GENERATED_MAIN
        [global::System.STAThreadAttribute]          // 仅 IsWin32App
        static void Main(string[] args)
#else
        internal static void XamlGeneratedMain()
#endif
        {
            global::WinRT.ComWrappersSupport.InitializeComWrappers();   // 仅 UsingCSWinRT
            ... XamlOptionalChanges 遍历 ...
            Application.Start((p) => {
                var context = new DispatcherQueueSynchronizationContext(...);
                SynchronizationContext.SetSynchronizationContext(context);
#if !DISABLE_XAML_GENERATED_MAIN
                new App();
#else
                App.XamlGeneratedCreateApplicationInstance();
#endif
            });
        }
    }
    partial class App : Application
    {
        // DISABLE 分支
        internal static void XamlGeneratedCreateApplicationInstance()
        {
            _XamlGeneratedCreateApplicationInstance();   // 可省略 partial → 静默空转
        }
        static partial void _XamlGeneratedCreateApplicationInstance();
    }
```

用户入口 delegate 样例：`Samples/DisableXamlGeneratedMain/Cs/Program.cs:16`（`XamlGeneratedProgram.XamlGeneratedMain()`）；无参构造缺失自写样例：`Samples/DisableXamlGeneratedMain/CsNoCtor/Program.cs:13-24`。

### 依赖

P0-3（MTA→STA 实证，确认条件化输出与运行）、P0-1（DISABLE 结构）。改动点 2.3 依赖 P0-1。

---

## F3. `VisualBasicPagePass2.tt` — `IsApplication` 分支（必需，补内层 partial 实体）

**文件**：`src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicPagePass2.tt`

现状：`:24` 有 `if (!Model.CodeInfo.IsApplication)` 守卫，App 的 pass2 只输出空 `Partial Class <App> End Class`（无 `IsApplication` 分支）。

改动（**D1 镜像方案下本文件必改**，补内层 `_XamlGeneratedCreateApplicationInstance` 的实体）：

```tt
<# if (Model.CodeInfo.IsApplication) #>
<# {#>
<#     bool hasParameterlessCtor = true; #>
<#     var appClassType = Model.CodeInfo.ClassType; #>
<#     if (appClassType != null && appClassType.UnderlyingType != null) #>
<#     { #>
<#         var ctor = appClassType.UnderlyingType.GetConstructor(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null); #>
<#         hasParameterlessCtor = ctor != null; #>
<#     } #>
    Partial Class <#=Model.CodeInfo.ClassName.ShortName#>
<#     if (hasParameterlessCtor) #>
<#     { #>
#If DISABLE_XAML_GENERATED_MAIN Then
        Private Shared Sub _XamlGeneratedCreateApplicationInstance()   ' 实现部分不带 Partial：对齐既有先例 VisualBasicTypeInfoPass2.tt:1052（声明部分在 AppPass1 带 Partial，空体）
            Dim _application As New <#=Model.CodeInfo.ClassName.ShortName#>()   ' 裸 New 语句 BC30035（P0-1 实证）；无参构造缺失时不输出 → 声明在、实现缺 → 调用被忽略（type-members.md:1151）
        End Sub
#End If
<#     } #>
    End Class
<# }#>
```

**C# 对照基准**：`CSharpPagePass2.tt:20-48`（`IsApplication` 分支 + `hasParameterlessCtor` 反射检测 `:27-33` + `static partial void _XamlGeneratedCreateApplicationInstance() { new App(); }` `:41-44`）。

**依赖**：P0-1（实证确认无参构造缺失时 VB partial 未实现的实际行为 = 静默，与 C# 一致，文档化）。

---

## F4. T4 重新生成 `.cs`（强制纪律）

| 源 `.tt` | 生成物 `.cs` | 触发任务 |
|----------|--------------|----------|
| `VisualBasicAppPass1.tt` | `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicAppPass1.cs` | P1-3 |
| `VisualBasicPagePass2.tt`（若改） | `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicPagePass2.cs` | P2-2 |

`FieldDefinition.cs` 是普通 C# 源（非 `.tt` 产物），直接改。

---

## F5. `Samples/DisableXamlGeneratedMain` 的 VB 对应示例（按 P0-1）

**文件**：`Samples/DisableXamlGeneratedMain/Vb/...`、`Samples/DisableXamlGeneratedMain/VbNoCtor/...`（新建）

镜像 C# 结构：

- `Vb/Program.vb`：自定义 `Sub Main`（Module 内，无 `Shared`），delegate 到生成的 `XamlGeneratedProgram.XamlGeneratedMain()`；保留 `LaunchMarker = "CustomMain"` 断言标记。
- `Vb/App.xaml.vb`：`Public Partial Class App : Inherits Application`，显式 `Public Sub New()` 调 `InitializeComponent()`；`OnLaunched` 建窗。
- `VbNoCtor/Program.vb`：按 P0-1 结论：D1 下该样本证明「无参构造 + DISABLE = 编译期失败」或改为完全自写入口（对齐 `CsNoCtor/Program.cs:13-24`）。

**C# 对照基准**：`Samples/DisableXamlGeneratedMain/Cs/Program.cs`、`CsNoCtor/Program.cs`。

**依赖**：P0-1。

---

## F6. 单元测试改动（断言层）

**文件**：`src/XamlCompiler/Tests/UnitTests/CodeGeneratorTests.cs`（或新增 `VbFeatureParityTests.cs`）

沿用现有 `TestHelper.GenerateCodeBehind` + `CodeGeneratorProjectContext`（纯内存生成，无副作用）：

| 断言 | 输入 | 期望输出 |
|------|------|----------|
| FieldModifier internal | Page 含 `<Button x:Name='btn' x:FieldModifier='internal'/>` | VB pass1/pass2 含 `Friend WithEvents btn` |
| FieldModifier public | 同上 `public` | `Public WithEvents btn` |
| 默认字段 | 无 `x:FieldModifier` | `Private WithEvents btn` |
| 入口点形态 | App.xaml 生成 VB pass1 | 含 `Public Module Program`、`Sub Main(ByVal args() As String)`（无 `Shared`）、`Sub New()`、无 `Sub Program`、无 `MTAThread` |
| STAThread 条件 | `IsWin32App` on/off | on 含 `<STAThread()>`，off 不含 |
| ComWrappers 条件 | `UsingCSWinRT` on/off | on 含 `InitializeComWrappers`，off 不含 |
| C# 不回归 | 同输入 | C# 输出仍为小写 `internal`/`public`/`private` 字段、`[STAThread]`、`InitializeComWrappers` |

> 生成-断言需要 T4 生成的 `.cs` 与生成器一致（P1-3 后跑）；需要 `DirectUISchemaContext`/`SchemaMode.ManagedRuntime`（现有 `TestHelper` 已提供，`CodeGeneratorTests.cs:19-20,39-41`）。

---

## 逐域验收对照（实现完成后）

| 域 | 可判验收 |
|----|----------|
| 1 入口点 | 生成的 `.g.i.vb`/`.g.vb` 过 vbc；e2e VB 应用启动；特性条件与 C# 逻辑一致（P0-3）；DISABLE 契约按 P0-1 结论可编译可运行（P2-1） |
| 2 UnhandledException | 生成代码保留 `AddHandler Me.UnhandledException`；e2e 后台异常冒泡（P0-5） |
| 3 DesignerGenerated | `Sub Program` 不再出现；`DesignerGenerated()` + `InitializeComponent` + `_contentLoaded` 保留；显式构造器策略按 P0-2 结论落地并断言 |
| 4 WithEvents | `Friend WithEvents` 可编译；默认 `Private WithEvents`；降级分支按 P0-4 结论落地并断言；C# 侧输出不变 |
