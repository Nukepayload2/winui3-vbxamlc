# 测试计划：VB 生成器特色对齐

- **状态**：plan（测试计划；P0 未决项标「待 P0-x」）
- **纪律**：`Catalysts/Restriction.md`（无副作用）；单元测试禁止网络 / 文件写入 / 启动进程 / 注册表写入；VB 生成代码的 vbc 编译验证是编译器固有验证，发生在测试基础设施内，作为端到端 gate 运行，不属单元副作用禁令。

## 0. 测试策略分层

| 层 | 目标 | 副作用 | 前置成本 |
|----|------|--------|----------|
| L1 单元断言 | 生成代码形态（字符串断言），纯内存 | 无 | 低（现有 `TestHelper`） |
| L2 vbc 编译 gate | 生成的 `.g.vb`/`.g.i.vb` 是合法 VB | 无（内存/临时编译产物，编译器固有验证） | 低-中（需 Roslyn VB 编译器；本机有 `dotnet`） |
| L3 端到端 WinUI3 VB 最小应用 | 生成代码 + 运行时真实行为（启动 / 同步上下文 / UnhandledException / WithEvents / DISABLE） | 启动进程（WinUI3 桌面应用） | **高**（需本地构建 WinUI3 工程，首次搭建成本；端到端从未验证） |
| L4 C# 不回归 | C# 生成器输出不变 | 无（L1 断言）或需编译（L2 gate） | 低 |

> L3 的「启动进程」是 WinUI3 运行时端到端验证的固有行为，与单元副作用禁令不冲突（禁令约束的是单元测试；L3 是明确的端到端 gate，需在测试基础设施内运行并由维护者显式触发）。

## 1. L1 单元测试（无副作用）

**文件**：`src/XamlCompiler/Tests/UnitTests/`（建议新增 `VbFeatureParityTests.cs`，或扩展现有 `CodeGeneratorTests.cs`）

沿用 `TestHelper.GenerateCodeBehind` + `CodeGeneratorProjectContext`（`CodeGeneratorTests.cs:39-56`，纯内存生成，无文件写入、无网络）。

| 用例 | 输入 | 断言（期望生成输出） | 对应改动 |
|------|------|----------------------|----------|
| U1-1 | Page：`<Button x:Name='btn' x:FieldModifier='internal'/>`，VB pass1/pass2 | 含 `Friend WithEvents btn`；不含 `Internal WithEvents` | F1（两构造器） |
| U1-2 | Page：`x:FieldModifier='public'` | 含 `Public WithEvents btn` | F1 |
| U1-3 | Page：无 `x:FieldModifier` | 含 `Private WithEvents btn`（默认槽位标题化） | F1-3 |
| U1-4 | App.xaml，VB pass1 | 含 `Public Module Program`；`Sub Main(ByVal args() As String)` 行不含 `Shared`；`InitializeComWrappers` 在 `Sub Main` 体内（不在 `Sub New`）；不含 `MTAThread`；不含 `Sub Program` | F2-1/F2-2 |
| U1-5 | App.xaml，`IsWin32App=true` vs `false` | 两者均不含 `<MTAThread()` 与 `<STAThread()`（P0-3 实证：不写线程模型特性即 STA） | F2-1 |
| U1-6 | App.xaml，`UsingCSWinRT=true` vs `false` | true 含 `Global.WinRT.ComWrappersSupport.InitializeComWrappers()` 与 `DispatcherQueueSynchronizationContext`；false 不含两者 | F2-1 |
| U1-7 | App.xaml，`EnabledXamlOptionalChanges={"5"}` / `{"MyChange"}` | 数值 → `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`；命名 → `Global.Microsoft.UI.Xaml.Settings.XamlChangeId.MyChange`；`DisabledXamlOptionalChanges` → `DisableChange(...)` | F2-1 |
| U1-8 | 上述同输入的 C# pass1/pass2 | 输出仍为小写 `internal`/`public`/`private`、`[STAThread]`、`InitializeComWrappers`（C# 零改动回归） | 全部 |
| U1-9 | DISABLE 分支 | 字符串断言：`Friend Module XamlGeneratedProgram`、`Friend Shared Sub XamlGeneratedCreateApplicationInstance`、`Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance` 存在性 | F2-3/F3 |
| U1-10 | WithEvents 降级（按 P0-4 结论 (ii) 注释） | 命中降级分支的类型输出普通字段（无 `WithEvents`）+ 注释 `' 值类型不支持 WithEvents，已输出普通字段` | F1/F2 降级 |

**验收**：`runtests.cmd` 下 U1-* 全绿（`vstest.console.exe` 跑 `UnitTests.dll`）。

## 2. L2 vbc 编译 gate

**目标**：生成代码是可编译的合法 VB；这是编译器固有验证，属测试基础设施。

**方法**：把 L1 生成的 `.g.i.vb`/`.g.vb` 内容喂给 Roslyn VB 编译器编译（最小 `.vbproj` 工程，引用 WinUI3 / CSWinRT 程序集桩），断言零错误。本机有 `dotnet`（Roslyn csc/vbc 二进制随 SDK 分发），可 `dotnet build` 最小工程或直接调 Roslyn 编译器二进制。

| 用例 | 内容 | 断言 |
|------|------|------|
| V1 | 含 `Friend WithEvents` / `Public WithEvents` / `Private WithEvents` 字段的 Page pass1/pass2 | vbc 零错误 |
| V2 | `Module Program`（`Sub Main` 内联初始化：ComWrappers + XamlOptionalChanges + Start lambda + 同步上下文；不写线程模型特性）的 App pass1 | vbc 零错误；验证 `Sub Main` 无 `Shared` 合法（BC30433 已实测，作为回归断言） |
| V3 | DISABLE 分支生成物（`Friend Shared` + `Private Shared Partial Sub` 两层钩子） | vbc 零错误；无参构造缺失时内层 partial 未实现 → 按 P0-1 实证的 VB 实际行为断言（预期静默，与 C# 一致） |
| V4 | `CType(5, Global...XamlChangeId)` 的 App pass1（`EnabledXamlOptionalChanges={"5"}`） | vbc 零错误 |
| V5 | C# 同输入生成的代码 | csc 零错误（C# 不回归编译） |

**验收**：V1-V5 全通过；VB 与 C# 对照编译均零错误。

## 3. L3 端到端 WinUI3 VB 最小应用（需本地构建 WinUI3 工程）

**目标**：生成代码 + 运行时真实行为。**这是本任务的关键缺口**——接手基线自述端到端从未验证、单元测试项目 current broken；需首次搭建本地 WinUI3 VB 最小应用（成本最高，建议 P0 阶段用最小样本一次性打通，之后作为回归资产复用）。

**前置**：本机需具备 WinUI3 桌面应用构建能力（Windows App SDK 包、MSBuild 目标 `Microsoft.UI.Xaml.Markup.Compiler.interop.targets`）、可启动 WinUI3 桌面应用的环境。

| 用例 | 内容 | 断言 | 对应 |
|------|------|------|------|
| E1 | 最小 VB App + Page，走新入口点（`Module Program`）启动 | 应用窗口出现、正常启动、无崩溃；`LaunchMarker` 类标记可断言 | F2 / P0-3 |
| E2 | 入口线程 STA（P0-3 实证：不写线程模型特性即 STA） | e2e 启动成功 + sta.log=STA（P0-3 已实证） | P0-3 |
| E3 | 同步上下文联动：后台线程抛异常 | 后台异常须经 **async/await 恢复路径 + 入口 DispatcherQueueSynchronizationContext** 才可靠触发 handler（P0-5 实证 case c：`Async Sub` `Await Task.Run(Sub() Throw ...)` → `unhandled.log` 出 `HANDLER_FIRED`、退出码 42）；**负例钉死**：纯 `Task.Run` unobserved 不触发不崩溃（超时 43）、`DispatcherQueue.TryEnqueue`/Timer 直抛不触发且崩溃（0xC000027B）、无同步上下文时 async 场景崩溃（0xE0434352）——播种后台异常不得用 DispatcherQueue 直抛；UI 线程未被后台异常击穿 = 仅 await 交付路径存活 | P0-5 |
| E4 | WithEvents：Page `x:Name` 字段 + 用户 `Handles` 事件处理器 | 事件触发时 handler 执行；`Friend`/`Public`/`Private` 三种修饰均可编译运行 | F1 |
| E5 | DesignerGenerated：显式构造器漏调 `InitializeComponent` 的页面 | 构建出现 BC40054；运行时行为按 P0-2 结论（若选 (a)：文档化，静默空白为预期并记录） | P0-2 |
| E6 | DISABLE：`DISABLE_XAML_GENERATED_MAIN` + 自定义入口 delegate | 应用经 `XamlGeneratedProgram.XamlGeneratedMain()` 启动（`LaunchMarker="CustomMain"`），初始化（ComWrappers/同步上下文）生效 | P0-1 / F2-3 |
| E7 | DISABLE + 无参构造缺失 | 内层 partial 未实现 → 按 P0-1 实证的 VB 行为断言（预期静默，与 C# 一致）；或自写入口路径可启动 | P0-1 |

**验收**：E1-E7 全部通过或按 P0 结论记录预期失败并断言；这是「无遗留问题」的收口证明。

## 4. L4 C# 不回归

- L1 的 U1-8 已覆盖 C# 输出形态断言。
- L2 的 V5 已覆盖 C# 编译。
- 回归项目：现有 `CodegenTests.cs` 的 C#/C++ diff 测试多为 `[Ignore]`（`CodegenTests.cs:180-406`），不阻塞本任务；但 **C# 生成器零改动**，理论上零回归。跑 `runtests.cmd` 全量确认现有通过的测试不因本任务失败。

## 5. 首次搭建成本（明确哪些需要本地 WinUI3 构建）

| 项 | 是否需要本地构建 WinUI3 工程 | 成本 | 说明 |
|----|------------------------------|------|------|
| L1 单元断言 | 否 | 低 | 纯内存生成，现有 `TestHelper` |
| L2 vbc gate | 否（但需 Roslyn VB 编译器） | 低-中 | `dotnet` 已在本机 |
| **L3 端到端（E1-E7）** | **是** | **高** | 首次搭建最小 WinUI3 VB 工程；需要 Windows App SDK 包 + 可启动桌面环境；建议 P0 阶段先用最小样本打通，作为回归资产复用 |
| L4 C# 不回归 | 否（`runtests.cmd` 需构建单元测试工程） | 中 | 需 vstest.console + LibManaged* 构建（`runtests.cmd:101-146` 已处理） |

> 若 L3 首次搭建在实现阶段受阻（缺 Windows App SDK / 无法启动桌面应用），**不得静默跳过**：按纪律向用户说明并询问（E1 的启动验证也可退化为「构建 + 生成代码 vbc 编译」的证据等级标记「待定」，但要显式记录影响，不以「已知后续项」收尾）。

## 6. 无副作用纪律声明

- L1 单元测试：纯内存 `GenerateCodeBehind`，无网络 / 文件写入 / 启动进程 / 注册表写入（`CodeGeneratorTests.cs` 现状即如此，新增用例遵循同模式）。
- L2 vbc 编译 gate：编译器固有验证，在测试基础设施内运行，属允许的端到端 gate；不触碰用户工程、不写注册表。
- L3 启动进程是 WinUI3 运行时验证的固有行为，作为显式端到端 gate 运行；不作为单元测试。
- 任何「写不了就测不了」的部分（如 L3 环境缺失）向用户说明并询问，不静默跳过。

## 7. 验收总纲

- P0 五项实证全部有结论（每项含「能编译/能跑通/能断言」的可判验收，见 `README.md` 代办清单）。
- P1+ 实现后：L1（U1-*）全绿、L2（V1-V5）全绿、L4 `runtests.cmd` 全量通过、L3（E1-E7）按 P0 结论全部通过或记录预期失败并断言。
- 无「已知后续项 / 文档化边界」收尾；每项收口带对应测试断言。
- 全量测试（单元 + VB 编译 gate + 端到端 + C# 不回归）通过后才关闭实现阶段。
