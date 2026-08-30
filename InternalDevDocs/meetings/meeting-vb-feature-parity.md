# Meeting: VB 生成器特色对齐

**Agenda:** [`InternalDevDocs/proposals/proposal-vb-feature-parity.md`](../proposals/proposal-vb-feature-parity.md)

**Proposal:** VB 生成器特色对齐——4 个 VB 特色域对齐 C# 功能
**_Related:_** 本 fork 接手基线 `InternalDevDocs/xaml-compiler-index.md`「VB 现状基线」；`docs/design-notes/xamlcompiler.md`

**状态:** LDM Reviewed — **Active**（方向与现状诊断成立；两处关键判断经 Roslyn 编译器源码取证落实；按本 RESOLUTION 进入 plan 阶段）
**判定:** Active（总体）；Active / Active / Active / Active（按 4 域分项，见 RESOLUTION）

---

## 讨论

### 域 1：入口点与初始化

**`Private Shared Partial Sub` 合法，C# `static partial void` 可逐字镜像。** Roslyn 的错误报告函数 `ReportPartialMethodErrors`（`roslyn SourceMethodSymbol.vb:144-213`）只对 `Public` / `MustOverride` / `NotOverridable` / `Overridable` / `Overrides` / `MustInherit` / `Protected` / `Friend`（及 `Protected Friend` / `Friend Protected`）报 `ERR_OnlyPrivatePartialMethods1`，**拒绝表里没有 `SharedKeyword`**；`PrivateKeyword` 出现即把 `reportPartialMethodsMustBePrivate` 置 False。VB 对 partial 的硬约束只有"可访问性必须 Private"，`Shared` 不在其列。测试库是更硬的证据：`EntryPointTests.vb:780,799` 直接编译 `Private Shared Partial Sub Main() / End Sub`，`BindingErrorTests.vb:14086,14109` 编译 `Private Shared Partial Sub M2(Of T)()` / `M3()`——除"对无实现 partial 取 `AddressOf`"报 BC31440 外，`Shared` + `Partial` 并存本身零诊断。spec 侧同样只有"必须 Private、必须是无语句的 Sub"（`spec\type-members.md:1167`），没有禁 Shared 的条款。

**两层可见性分别映射。** C# 侧有两个不同可见性的成员叠在一起：外层 `XamlGeneratedCreateApplicationInstance()` 是 `internal static`（`CSharpAppPass1.tt:81`），内层 partial `_XamlGeneratedCreateApplicationInstance()` 是 `static partial void`（C# partial 无显式可见性默认 private，`:88`）。裁决：外层镜像 `internal static` → **`Friend Shared`**；内层镜像 private partial → **`Private Shared Partial`**。这样既逐字对齐 C# 语义，又满足 VB"partial 必须 Private"的硬约束。

**DISABLE 契约结构：**

```vb
#If Not DISABLE_XAML_GENERATED_MAIN Then
Public Module Program
    Sub Main(ByVal args() As String)          ' 模块内方法隐式 Shared，勿写 Shared（BC30433）
    <# if (ProjectInfo.IsWin32App) { #>
    <STAThread()> _
    <# } #>
        ' 内联：ComWrappers(仅 UsingCSWinRT) / XamlOptionalChanges 两遍历 / Application.Start(...New App())
    End Sub
End Module
#Else
Friend Module XamlGeneratedProgram
    Friend Sub XamlGeneratedMain()            ' DISABLE 契约入口（不叫 Main，不构成启动对象）
        ' 内联同一段初始化 + Application.Start(...App.XamlGeneratedCreateApplicationInstance())
    End Sub
End Module
#End If

Partial Class <App>
    Friend Shared Sub XamlGeneratedCreateApplicationInstance()
        _XamlGeneratedCreateApplicationInstance()
    End Sub
    Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()
    End Sub                                   ' 定义声明（空体）；pass2 在 App 有无参构造时补实现
End Class
```

- **pass1 可用性闭环**：`XamlGeneratedProgram` 和 App 级 helper 都在 pass1 模板里输出，用户自定义入口（pass1 中间编译时已编译）引用 `XamlGeneratedProgram.XamlGeneratedMain()` 立即可解析。内层 partial 的实现体由 pass2 的 `VisualBasicPagePass2.tt` 新增 `IsApplication` 分支补（镜像 `CSharpPagePass2.tt:20-48` 的无参构造检测）；pass2 未补体时，VB partial 擦除语义让调用被忽略、pass1 中间程序集照常可编译（`spec\type-members.md:1151`；C# 侧同构，`roslyn MethodCompiler.cs:680-688`）。
- **入口点候选边界**：DISABLE 分支的方法叫 `XamlGeneratedMain` 而非 `Main`，不会误成启动对象——C# 侧 `MethodSymbol.cs:728-732` 明确"声明而无实现的 partial 不构成 Main"，VB 侧同名规则成立。
- **NoCtor 语义差**：VB 无参构造缺失时 `_XamlGeneratedCreateApplicationInstance` 无实现、方法不存在，用户引用 helper 直接编译失败（**fail-fast**），与 C#「静默空转」（`CsNoCtor/Program.cs:13-24` 里用户手动写整套初始化）语义不同。这个差异要实现时写进文档。补 VB 版 `DisableXamlGeneratedMain` 示例（含 NoCtor）作为 plan 阶段的固化动作。
- **外层可见性**：`Friend Shared`（对齐 C# `internal static`），不默认 `Public`。

**初始化归属：内联于 `Sub Main` / `XamlGeneratedMain` 体，不由 `Sub New` 承担。** 理由有三：(a) C# 就是把初始化内联在同一段体里，DISABLE 分支复用同一段（`CSharpAppPass1.tt:30-67`），VB 内联才能让非 DISABLE 与 DISABLE 两分支**共享同一份初始化文本**，DISABLE 用户才拿得到与 C# 对等的 ComWrappers / XamlOptionalChanges；(b) "Module `Sub New` + `#If Not DISABLE`"的归属割裂会让 DISABLE 用户裸奔；(c) 模块 `Sub New` 的时序保证（`types.md:728,733`、`type-members.md:1267-1271`，先于任何模块成员访问）没有错，但契约一致性压过时序习惯。**Module 容器保留**（VB 正统入口、不可实例化），只是初始化从 `Sub New` 挪进 `Sub Main` 体。同步上下文与 C# 对齐，**仅 `UsingCSWinRT` 分支设置**（`CSharpAppPass1.tt:48-57` vs `:58-66`），非 CSWinRT 分支不设——提案草稿的无条件设置是对 C# 基准的偏离，驳回。

**草稿语法修正（全部保留）：** Module 内 `Sub Main` 不写 `Shared`（`spec\type-members.md:551`）；`#If UsingCSWinRT Then` 是伪代码，`UsingCSWinRT`/`IsWin32App` 是 `XamlProjectInfo.cs:118,120` 的属性、由 `CompileXamlInternal.cs:1913-1914` 赋值，**必须 T4 时条件** `<# if (ProjectInfo.UsingCSWinRT) { #>`；`XamlOptionalChanges`/`XamlChangeId` 在 `Microsoft.UI.Xaml.Settings`，走 `Globalize`，数值 change id 用 `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`、枚举名用 `Global.Microsoft.UI.Xaml.Settings.XamlChangeId.Name`（`CSharpAppPass1.tt:43,46` 的 `(XamlChangeId)` 转型是 C# 语法，VB 不照抄）；`New Global.<AppShortName>()` 单括号。

### 域 2：UnhandledException——保留，联动依赖域 1

`VisualBasicAppPass1.tt:76-83` 的 `AddHandler Me.UnhandledException, Sub(...) Debugger.Break()` 与 C# `CSharpAppPass1.tt:127-132` 逻辑等价，`#If Debug AndAlso Not DISABLE...` 写法规范合法（`spec\preprocessing-directives.md:53-58` 含 `AndAlso`）。不改。域 1 补上 `DispatcherQueueSynchronizationContext` 后后台线程异常才可靠冒泡到 `Application.UnhandledException` 的联动推理成立——`Async Sub` 起始时若 `SynchronizationContext.Current` 为 Nothing 异常发线程池，非 Nothing 则走同步上下文重抛（`spec\statements.md:162-170`）。正确性依赖域 1 落地，端到端标「待定」。

### 域 3：DesignerGenerated——机理落实

**`InitializeInstance` 不存在。** `GetDesignerInitializeComponentMethod`（`roslyn MethodCompiler.vb:864-879`）在类带 `DesignerGeneratedAttribute` 且存在非共享、非泛型、零参 `Sub InitializeComponent` 时记为 `InitializeComponentOpt`；注入点在 `InitializerRewriter.vb:150-158`——`If Not constructorMethod.IsShared AndAlso compilationState.InitializeComponentOpt IsNot Nothing AndAlso constructorMethod.IsImplicitlyDeclared`，**只对隐式（编译器发射的默认）构造器**、在基类调用与 Handles 挂钩之后（即构造器**末尾**，不是"开头"）注入 `Me.InitializeComponent()`。显式构造器漏调只在 `MethodCompiler.vb:770-778` 发 **BC40054** 警告、不注入。这是对 `spec\type-members.md:1265`（"emitted into a designer generated class"的默认构造器才会调 `InitializeComponent`）的准确展开。提案 §3 的"生成 Sub InitializeInstance() 并在每个构造器开头注入"表述作废。

设计影响：WinUI App/Page 写显式 `Sub New` 是常态，漏调则**构建仅警告、运行静默空白**。域 3 口径 = **显式构造器策略**三选一（文档化 + 依赖 BC40054 / 维持现状 / 生成代码兜底），具体选哪个归 plan 阶段用最小 VB 页面实证定。`_contentLoaded` 守卫与自动钩叠加防重复加载的机制安全（`VisualBasicAppPass1.tt:60-64`）。`Sub Program` 清理维持：`VisualBasicAppPass1.tt:31-34` 的无参实例方法，VB 构造器恒为 `Sub New`，无体、无引用、默认 `Public`，删除安全。

### 域 4：WithEvents / x:FieldModifier——回归实锤，边界收窄

`internal`→`Internal` 是回归而非新 bug。UWP 时代 master `FieldModifierTests.g.i.vb:41` 输出 `Friend WithEvents Internal1 As Global.Windows.UI.Xaml.Controls.TextBlock`——`Friend` 才是历史正确输出，`ToTitleCase()`（`StringExtensions.cs:28-38`，只首字母大写，`"internal"`→`"Internal"`）把历史行为回归坏了。`LanguageSpecificString` 构造序是 `(cppCX, cppWinRT, cs, vb)`（`LanguageSpecificString.cs:13-23`），所以 `FieldDefinition.cs:49-54` 的 VB 槽位确实是 `modifier.ToTitleCase()`，`internal` 变 `Internal` 不可编译（`Internal` 不在 VB 关键字表，`spec\lexical-grammar.md:357-403`）。**修法覆盖三处槽位**：ctor1（`:49-54`）VB 委托 `internal`→`Friend`、其余保持标题化；ctor2（`:85`，pass1 本地类型路径）是四语言**同串原样透传** `new LanguageSpecificString(() => modifier)`，`internal`/`public` 原样小写同样不可编译（`public` 小写因 VB 大小写不敏感恰好合法，`internal` 无效），必须改为 VB 关键字映射；默认槽位（`:95-99`）VB 是小写 `"private"`（合法、风格不统一，次要，建议统一 `Private`）。C# 槽位（`ToLower`）与 C++ 槽位不受影响，`LanguageSpecificString` 本就四语言参数化，共享模型层零连坐。

**WithEvents 边界收窄为唯一硬禁忌 = 值类型。** `spec\type-members.md:2017` 明确"not valid ... if the variable is typed as a structure"；Roslyn `ComputeWithEventsFieldType`（`roslyn SourceMemberFieldSymbol.vb:218-239`）落实：数组 → `ERR_EventSourceIsArray`（BC30415）、非（类/接口/带引用约束的类型参数）→ `ERR_WithEventsAsStruct`（BC30413）、**接口类型合法**（`IsClassOrInterfaceType` 覆盖接口）。"无事件的接口"不是编译错误——`WithEvents` 只是把字段换成同名属性做钩子（`spec\type-members.md:1978-2015`），没有 `Handles` 引用它时 setter 是纯赋值、空转。因此**降级分支只需按 `FieldDefinition.IsValueType`（`:30,64` 已计算）触发**（+ 数组防御），"无事件接口"降级这条理论化路径删掉。降级的用户可见行为（静默 vs 注释/警告）等枚举命中类型面再定（预期极少，`x:Name` 字段绝大多数是 `FrameworkElement`/`DependencyObject` 派生）。`PagePass2 Connect` 里 `Me.<FieldName> = CType(target, ...)`（`VisualBasicPagePass2.tt:116`）走 WithEvents 属性 setter 完成挂钩，是设计好的既有行为，不动。

### 如何融合两份老登意见

两位老登均为「附带条件支持、置信度高」，关键事实全部收敛到 Roslyn 源码。分歧只剩两处，都按证据裁决：(1) 外层可见性 `Private` vs `Friend`——按"两层分别映射"解决（外层 `Friend Shared`、内层 `Private Shared Partial`）；(2) 初始化归属 `Sub New` vs 内联——按"DISABLE 契约一致性"解决（内联，Module 容器保留）。VB 老登的正宗习惯表述（Module、`Friend`、`AddHandler`/`AndAlso`、`End Sub)` 多行 lambda 收尾合法，`roslyn` 测试源大量使用）与 C# 老登的契约结构（内联体复用、`XamlGeneratedProgram` 等价物、pass2 App 分支）互为补充，融合后的结构是**可编译、与 C# 逻辑等价、且 VB-like** 的。

---

## RESOLUTION

**三态判定：Active**。按域分项：

1. **入口点与初始化（Active）**
   - **DISABLE 钩子**：`Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance()`（定义声明空体）合法且逐字镜像 C# `static partial void`（`roslyn SourceMethodSymbol.vb:144-213`；`EntryPointTests.vb:780,799`；`BindingErrorTests.vb:14086,14109`；`spec\type-members.md:1167`）。**两层可见性分别映射**：外层 `XamlGeneratedCreateApplicationInstance` = `Friend Shared`（镜像 C# `internal static`，`CSharpAppPass1.tt:81`）；内层 partial = `Private Shared Partial`（镜像 C# private partial，`:88`）。
   - **初始化归属**：弃「Module `Sub New` 做初始化 + `#If Not DISABLE` 割裂」，改为**内联于 `Sub Main` / `XamlGeneratedMain` 体**（对齐 C# `CSharpAppPass1.tt:30-67`），两分支共享同一份初始化文本。Module 容器保留（`Module Program` / `Friend Module XamlGeneratedProgram`）。
   - **DISABLE 契约**：`#If Not DISABLE_XAML_GENERATED_MAIN` 走 `Module Program` + `Sub Main`，`#Else` 走 `Friend Module XamlGeneratedProgram` + `Friend Sub XamlGeneratedMain()`（内联初始化 + `Application.Start(...App.XamlGeneratedCreateApplicationInstance())`）。`VisualBasicPagePass2.tt` 新增 `IsApplication` 分支（镜像 `CSharpPagePass2.tt:20-48`），仅在 App 有无参构造时补 `_XamlGeneratedCreateApplicationInstance` 实现体；pass1 可用性由 VB partial 擦除保证（`spec\type-members.md:1151`）。
   - **语法裁决**：Module `Sub Main` 不写 `Shared`（`spec\type-members.md:551`）；`<STAThread()>` 仅 `IsWin32App` T4 时条件；`UsingCSWinRT`/`IsWin32App` 走 T4 控制块、禁止 `#If UsingCSWinRT Then` 伪代码；`XamlOptionalChanges`/`XamlChangeId` `Globalize` + `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`；同步上下文**仅 `UsingCSWinRT` 分支设置**；`New Global.<AppShortName>()` 单括号。
   - **NoCtor 语义**：VB 方法不存在 = 编译期 fail-fast，与 C#「静默空转」差异需写明；补 VB `DisableXamlGeneratedMain` 示例（含 NoCtor）。

2. **Application UnhandledException（Active，维持）**
   - 保留 `AddHandler Me.UnhandledException` 调试挂钩（`VisualBasicAppPass1.tt:76-83`）不动；正确性依赖域 1 同步上下文联动，端到端标「待定」。

3. **DesignerGenerated（Active）**
   - **机理**：`InitializeInstance` 不存在；`roslyn InitializerRewriter.vb:150-158` 只对**隐式默认构造器**、在构造器**末尾**注入 `InitializeComponent`；显式构造器漏调仅 `MethodCompiler.vb:770-778` 发 **BC40054**、不注入（`spec\type-members.md:1265`）。提案「每个构造器开头注入」表述作废。
   - 域 3 口径 = **显式构造器策略**三选一（文档化 + BC40054 / 维持现状 / 生成代码兜底），plan 阶段定（OPEN QUESTION）。
   - 保留 `DesignerGeneratedAttribute` + `Public Sub InitializeComponent()` + `_contentLoaded` 守卫组合；删除 `Sub Program`（`VisualBasicAppPass1.tt:31-34`）。

4. **code-behind x:Name 映射 WithEvents（Active，维持 + 收窄）**
   - `FieldDefinition` **三个槽位**统一 VB 关键字映射：ctor1（`:49-54`，`ToTitleCase`）与 ctor2（`:85`，四语言同串）都做 `internal`→`Friend`、其余按 VB 关键字首字母大写；默认槽位（`:95-99`）风格统一为 `Private`（次要）。历史佐证 `TestMasters/.../FieldModifierTests.g.i.vb:41` = `Friend WithEvents`，`ToTitleCase` 是回归。
   - **WithEvents 降级条件收窄为 `FieldDefinition.IsValueType`（+ 数组防御）**，"无事件接口"降级删除（`spec\type-members.md:2017`；`roslyn SourceMemberFieldSymbol.vb:218-239`：数组 BC30415、值类型 BC30413、接口合法）。降级用户可见行为（静默 vs 注释/警告）待命中类型面枚举后定（OPEN QUESTION）。

**Implication:**
- 改动集中在 `VisualBasicAppPass1.tt`（入口 + App helper + `XamlGeneratedProgram`）、`VisualBasicPagePass2.tt`（新增 `IsApplication` 分支）、`FieldDefinition.cs`（VB 槽位映射）。C# 生成器与共享模型层（`LanguageSpecificString`/`TypeForCodeGen`/`Language`）零连坐。
- `.tt` 是源、`.cs` 是生成物；C# 侧零改动，不触发"改 C# 生成器同步 VB"纪律。
- 行为变更：VB 生成代码首次补入口初始化（MTA→STA、同步上下文、ComWrappers、XamlOptionalChanges），现有 VB 应用未实证前不得默认运行行为不变。
- 风险收敛：DISABLE 契约结构已闭环为可编译结构；剩余风险全在运行时实证（真 WinUI3 工程）。

**OPEN QUESTIONS / TODO / Follow-up:**
1. **DesignerGenerated 显式构造器策略**（(a) 文档化 + BC40054 / (b) 维持现状 / (c) 生成代码兜底）——plan 阶段最小 VB 页面实证后选一。
2. **真 WinUI3 运行时实证（待定）**：MTA→STA 对既有 VB 应用的兼容影响；同步上下文联动后后台线程异常可靠冒泡到 `Application.UnhandledException`；DesignerGenerated 自动钩与 `_contentLoaded` 守卫在 CSWinRT / 真 App 基类下的组合行为。
3. **WithEvents 降级命中类型面**：枚举哪些 XAML `x:Name` 字段类型是值类型/数组；降级用户可见行为（静默 vs 注释/警告）。
4. **Module Program 与 `<StartupObject>` 接线**：模块全限定名 `RootNamespace.Program` 的启动对象语义需最小应用实测（待定）。
5. **Sub Program 删除回归**：跑一次端到端确认无隐藏依赖（已确认非构造、无体、无引用）。
6. 补 VB `DisableXamlGeneratedMain` 示例（含 NoCtor 变体），固化 DISABLE 契约。

---

## 附录：五维评审（独立评审为不入库工作材料）

独立五维评审（正确性/可编译性、C# 基准一致性、完整性、实施可行性、证据充分性）为不入库工作材料，本纪要正文不展开。概要：草案硬伤全部修正，DISABLE 契约闭环，五维总分约 3.9/5。
