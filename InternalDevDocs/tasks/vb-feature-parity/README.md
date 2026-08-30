# 任务：VB 生成器特色对齐（入口点初始化 / UnhandledException / DesignerGenerated / WithEvents）

- **阶段**：plan（任务计划 + 测试计划）
- **上游依据**：`InternalDevDocs/meetings/meeting-vb-feature-parity.md`（RESOLUTION 唯一权威）
- **提案**：`InternalDevDocs/proposals/proposal-vb-feature-parity.md`（含背景；其 Detailed design 草稿有硬伤，一律以 RESOLUTION 修正为准，不采信草稿细节）
- **两老登实证意见**（Roslyn/spec 取证）：`tmp/meetings/vb-feature-parity/vb-veteran.md`、`tmp/meetings/vb-feature-parity/csharp-veteran.md`
- **源码地图**：`InternalDevDocs/xaml-compiler-index.md`

## 任务总览

把 4 个 VB 特色域对齐 C# 功能。VB 生成器骨架已存在且随 C# 同步，但入口点初始化缺失、`x:FieldModifier` 的 VB 关键字映射有硬伤、`Sub Program` 遗留污染公开面、DesignerGenerated 显式构造器策略与 WithEvents 边界未定。

| 域 | 内容 | 状态（相对 C#） |
|----|------|-----------------|
| 1 | 入口点与初始化（Module Program / Sub New / Sub Main / DISABLE 契约） | 缺失，需重设计 |
| 2 | `Application.UnhandledException` 调试挂钩 | 已等价输出，保留不动 |
| 3 | `DesignerGenerated` 自动钩 + `Sub Program` 清理 | 保留，口径改「显式构造器策略」 |
| 4 | code-behind `x:Name` 映射 `WithEvents` / `x:FieldModifier` | 硬伤（`internal`→`Internal` 不可编译），需修 |

## RESOLUTION 硬性裁决（不可重新裁决，实现必须遵守）

1. **设计 A 采纳**：`Module Program`（容器），**初始化内联于 `Sub Main`/`XamlGeneratedMain` 体**（对齐 `CSharpAppPass1.tt:30-67`，两 DISABLE 分支共享同一段初始化；**不再由 `Sub New` 承担初始化**）。**Module 内 `Sub Main` 不得写 `Shared`**（BC30433 实测）。同步上下文仅 `UsingCSWinRT` 分支设置。`New App()` 仅无参构造时输出。
2. **`#If UsingCSWinRT` 是伪代码**：`UsingCSWinRT` 是 `XamlProjectInfo` 属性（`CompileXamlInternal.cs:330` 赋值），必须 T4 时条件判断（`<# if (ProjectInfo.UsingCSWinRT) { #>`，对齐 `CSharpAppPass1.tt:39`）。
3. **`XamlOptionalChanges`/`XamlChangeId` 必须 Globalize**（`VB_CodeGenerator.cs:26-29`），数值 change id 用 `CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)`，不写 C# 转型。
4. **DISABLE 契约**：`Private Shared Partial Sub` **合法**（Roslyn `SourceMethodSymbol.vb:144-213` 只拒非 Private 可访问性；`EntryPointTests.vb:780,799`、`BindingErrorTests.vb:14086,14109`；spec `type-members.md:1167`），C# `static partial void` **可镜像**。**两层可见性分别映射**：外层 `XamlGeneratedCreateApplicationInstance` = `Friend Shared`（镜像 C# `internal static`，`CSharpAppPass1.tt:81`）；内层 `_XamlGeneratedCreateApplicationInstance` = `Private Shared Partial`（镜像 C# `private static partial`，`:88`）。DISABLE helper 携带 ComWrappers + XamlOptionalChanges + 同步上下文 + Start（全部内联）；无参构造缺失语义预期 = VB 静默空转（与 C# 一致：spec `type-members.md:1151` 无实现时调用被忽略 + `_AddOtherProvider` 生产先例），P0-1 实证确认。pass2 整体输出回退不再需要。
5. **DesignerGenerated 自动钩仅隐式默认构造器生效**（实测：显式 `Sub New` 漏调 `InitializeComponent` 只发 BC40054 警告、运行静默空白）。显式构造器策略三选一需 P0 实证/决策。
6. **`x:FieldModifier` internal→Friend**：须同时覆盖 `FieldDefinition.cs` 两个构造器（`:49-54` 与 `:85`，后者四语言同串非标题化）；默认槽位（`:96-99`）VB 统一为 `Private`（次要）。
7. **UnhandledException 保留不动**，正确性依赖同步上下文联动，端到端标「待定」。
8. **删除 `Sub Program`**（`VisualBasicAppPass1.tt:33`，VB 构造器恒为 `Sub New`，无引用、默认 `Public`，删除无风险）。

## 代办清单（P0 = 实证/摸底；P1+ = 实现）

### P0 实证任务（5 个 OPEN QUESTIONS，全部解决后才进 P1）

| 编号 | 任务 | OPEN QUESTION | 验收条件（可判） | 产出 |
|------|------|---------------|-------------------|------|
| P0-1 | DISABLE 契约实证与样例（**最高优先**） | OQ1（已定结构）：`Private Shared Partial Sub` 镜像 C# `static partial void`（外层 `Friend Shared` + 内层 `Private Shared Partial`）；无参构造缺失预期 = 静默（spec `type-members.md:1151` + `_AddOtherProvider` 先例） | 建最小 VB DISABLE 示例（vbc 可编译或 e2e）验证结构落地 + 产出可编译生成代码样例；实证确认 VB 无参构造缺失 = 静默空转（与 C# 一致）；建 `Samples/DisableXamlGeneratedMain` 的 VB 对应示例 | design-detailed 增补 DISABLE 节 + sample 骨架 |
| P0-2 | DesignerGenerated 显式构造器策略实证 | OQ2：(a) 文档化+依赖 BC40054 / (b) 维持现状 / (c) 生成基类调用链 三选一 | 最小 VB 页面实证：显式 `Sub New` 漏调 `InitializeComponent` 出现 BC40054；选定策略并给断言（若 (a)：断言生成代码不额外输出且文档注明；若 (c)：给出基类调用链设计 + 编译 gate） | design-detailed 增补策略结论 |
| P0-3 | 撤销 MTA 覆盖实证（VB 默认 STA） | OQ3（已实锤）：现模板 `<MTAThread()>` 主动把默认 STA 覆盖成 MTA（错误方向）；删特性即回归 VB 语言默认 STA（Roslyn `SourceMethodSymbol.vb:1438-1456`） | e2e 最小 VB 应用（**手写入口，不依赖 P1-2 生成器改动**，同时构成 L3 回归资产）：不写线程模型特性时入口线程 STA、应用启动成功 | design-detailed 增补实证结论 |
| P0-4 | WithEvents 边界 | OQ4（收窄）：唯一硬禁忌 = 值类型 `FieldDefinition.IsValueType`（spec `type-members.md:2017` + Roslyn `SourceMemberFieldSymbol.vb:218-239`：值类型 BC30413、数组 BC30476、**接口合法**；BC30415 实为 RedimRankMismatch） | 枚举命中值类型的类型面；选定降级用户可见行为并给断言（P0-4 实证：选定 (ii) 注释） | design-detailed 增补降级设计 |
| P0-5 | 同步上下文跨线程异常验证 | OQ5：入口补同步上下文后，后台线程异常是否可靠冒泡到 `Application.UnhandledException` | e2e：后台线程抛异常 → 断言 `Application.UnhandledException` handler 触发 | test-plan 增补 e2e 用例 |

> P0 全部完成后，P0 结论回写本 README「已决事项」与 design-detailed，然后进入 P1+。P0-1 阻塞 P2-1/P2-2/P3-1；P0-2 阻塞 P2-3；P0-3 阻塞 P1-2；P0-4 阻塞 P1-1（仅当降级涉及字段输出形态）；P0-5 阻塞 P4-2。

### P1+ 实现任务（逐文件）

| 编号 | 任务 | 文件 | 依赖 |
|------|------|------|------|
| P1-1 | `FieldDefinition.cs` VB 槽位映射（`internal`→`Friend`，两个构造器 + 默认槽位） | `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/FieldDefinition.cs` | P0-4（降级行为若涉及字段输出） |
| P1-2 | `VisualBasicAppPass1.tt` 入口点重设计（`Module Program` / `Sub Main` 内联初始化：ComWrappers + XamlOptionalChanges 双遍历 + Start lambda + 同步上下文，均 T4 时条件；Start lambda 内 `Dim _application As New ...`；**不写线程模型特性**——VB 默认 STA，P0-3 实证）+ 删除 `Sub Program` | `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicAppPass1.tt` | P0-3 |
| P1-3 | 用 T4 重新生成 `VisualBasicAppPass1.cs` 并 diff | `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicAppPass1.cs` | P1-2 |
| P2-1 | DISABLE 分支重设计（按 P0-1 结论：pass1 或 pass2 输出 `XamlGeneratedProgram` 等价物 + App 启动辅助 + `New App()` 条件） | `VisualBasicAppPass1.tt` 和/或 `src/XamlCompiler/BuildTasks/Microsoft/Xaml/XamlCompiler/CodeGenerators/VisualBasicPagePass2.tt` | **P0-1** |
| P2-2 | `VisualBasicPagePass2.tt` 增加 `IsApplication` 分支 + App 无参构造检测（若 P0-1 选 pass2 方案） | `VisualBasicPagePass2.tt` + 对应 `.cs` | P0-1 |
| P2-3 | DesignerGenerated 显式构造器策略落地（P0-2 已选 (a)：文档化 + 依赖 BC40054；确认型无源码改动） | `VisualBasicAppPass1.tt` / `VisualBasicPagePass1.tt`（确认现状已符合） | **P0-2** |
| P3-1 | `Samples/DisableXamlGeneratedMain` 的 VB 对应示例（按 P0-1 结论） | `Samples/DisableXamlGeneratedMain/Vb/...` 与 `VbNoCtor/...`（新建） | P0-1 |
| P4-1 | 单元测试：`FieldModifier`/入口点字符串断言 + vbc 编译 gate | `src/XamlCompiler/Tests/UnitTests/` | P1-1, P1-2, P2-1 |
| P4-2 | 端到端：最小 WinUI3 VB 应用（启动 + 同步上下文 + UnhandledException + WithEvents + DISABLE） | `src/XamlCompiler/Tests/` 或独立样本工程 | 全部 P1/P2 |
| P4-3 | C# 不回归 gate + 全量测试 | `runtests.cmd` | 全部 |

## 状态跟踪

| 阶段 | 状态 | 证据/说明 |
|------|------|-----------|
| plan | 进行中 | 本文档 + design-overview + design-detailed + test-plan 已产出 |
| P0-1 | 已完成 | 两层钩子 vbc 零错误样例（tmp/p0-1）；无参构造缺失 = 静默（与 C# 一致）；实现部分不带 Partial（BC31433）；裸 New 语句 BC30035 → 设计已改 Dim |
| P0-2 | 已完成 | BC40054 复现 + 隐式构造器自动注入实证（tmp/p0-2）；策略 (a) 文档化 + 依赖 BC40054 |
| P0-3 | 已完成 | VB 默认 STA 实锤（SourceMethodSymbol.vb:1438-1456 + Probe 运行时）；e2e 最小 VB WinUI3 应用构建 0 错误、启动成功、sta.log=STA（tmp/p0-3）；发现独立 bug：RootNamespace 前插 E2e.E2e（workaround Global.E2e） |
| P0-4 | 已完成 | BC30413 值类型 / BC30476 数组 / 接口合法；WMC0045 上游拦截；降级行为 (ii) 注释（tmp/p0-4） |
| P0-5 | 已完成 | 后台异常须 async/await 恢复 + 入口 DispatcherQueueSynchronizationContext 才可靠触发 UnhandledException（case c 退出码 42）；负例钉死（tmp/p0-5） |
| P1-1 | 已完成 | `FieldDefinition.cs` 三处 VB 槽位映射，双 TFM 编译零错误 |
| P1-2 | 已完成 | `VisualBasicAppPass1.tt` 入口重设计（与定稿逐字吻合，验证通过） |
| P1-3 | 已完成 | T4 重新生成 `VisualBasicAppPass1.cs`（VS MSBuild `PreprocessTemplates` 打通） |
| P2-1 | 已完成 | DISABLE 并列块 + 两层钩子 + `WriteCommonInit` 共享（AppPass1.tt/.cs），双 TFM 编译零错误 |
| P2-2 | 已完成 | PagePass2 `IsApplication` 分支补内层实体（PagePass2.tt/.cs），编译零错误 |
| P2-3 | 已完成 | DesignerGenerated 策略 (a) 落地：确认型无源码改动（现状已符合） |
| P3-1 | 已完成 | Samples Vb/VbNoCtor 构建成功（离线），DISABLE 契约端到端实证 |
| P4-1 | 已完成 | VbFeatureParityTests.cs（U1-1..U1-10 + V1-V5）产出并验证；U1-10 降级缺口修复完成；测试基建为已知限制（用户决策端到端+源码锚定验证） |
| P4-2 | 已完成 | 非 DISABLE 生成 Module Program 运行时 e2e（启动/STA/WithEvents Handles）；E1-E7 全覆盖 |
| P4-3 | 已完成 | C# 生成器零改动确认（CSharp*.tt/.cs 零改动 + FieldDefinition.cs C# 槽位保持）+ net8.0 编译 0 错误 + 全量收口（测试基建为已知限制） |
| 实现关闭 | 未开始 | 需全量测试（单元 + VB 回归 + 端到端）通过，无「已知后续项」收尾 |

## 关键纪律

- **证据阶梯**：每个决策标注 未提供 / 已检查 / 已运行 / 待定；文件存在只证明「已提供」，不写死未核实内容。
- **`.tt` 是源，`.cs` 是生成物**：改完 `.tt` 必须用 T4 工具重新生成对应 `.cs`（P1-3 为强制步骤），`.cs` 提交在库。
- **C# 生成器零改动**：本任务改动全部在 VB 模板 + `FieldDefinition.cs` VB 槽位 + 共享模型层（`LanguageSpecificString`/`TypeForCodeGen`/`Language`）无需连坐；`CompileXaml.cs:264-271` 的 VB TaskFileManager workaround 与本任务正交。
- **无副作用测试**：单元测试禁止网络/文件写入/启动进程/注册表写入；VB 生成代码的 vbc 编译验证是编译器固有验证，属测试基础设施内的端到端 gate，不违反禁令。
- **无遗留问题**：实现阶段不留「已知后续项/文档化边界」收尾；每项收口必须带对应测试断言；完成后跑全量测试。
- **实施-验证循环**：general-purpose background agent 串行交替（实施者→验证者），进度由 TaskCreate/TaskUpdate 驱动，main 只调度不催促。

## 已决事项（plan 阶段依据 RESOLUTION 锁定）

- **P0 五项实证全部完成**（详见 design-detailed「实证结果（P0-x）」各小节）：DISABLE 两层钩子 vbc 可编译、无参构造缺失 = 静默、实现部分不带 Partial（BC31433）、裸 `New` 语句需 `Dim`（BC30035）；DesignerGenerated 策略 (a) 文档化 + 依赖 BC40054；VB 默认 STA（删 `<MTAThread()` 即可，不写特性即 STA）；WithEvents 值类型 BC30413 / 数组 BC30476 / 接口合法、降级 (ii) 注释；同步上下文 + async/await 恢复路径才可靠触发 UnhandledException。
- 入口容器用 `Module Program`（非 Class）；`Sub Main` 无 `Shared`。
- **初始化内联于 `Sub Main`/`XamlGeneratedMain` 体**（对齐 C# `CSharpAppPass1.tt:30-67`，两 DISABLE 分支共享）；**不**由 `Sub New` 承担。
- DISABLE 钩子两层：外层 `XamlGeneratedCreateApplicationInstance` = `Friend Shared`；内层 `_XamlGeneratedCreateApplicationInstance` = `Private Shared Partial`。
- **不写线程模型特性**：删 `<MTAThread()`，VB 编译入口点默认注入 `STAThreadAttribute`（P0-3 实证 `SourceMethodSymbol.vb:1438-1456`），不写即 STA；`InitializeComWrappers()` 仅 `UsingCSWinRT` 时输出；均为 T4 时条件，不写 `#If UsingCSWinRT Then`。
- Start lambda 用 `Sub(p)` 形态，内设 `Global.Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext` + `Global.System.Threading.SynchronizationContext.SetSynchronizationContext`，末尾 `Dim _application As New Global.<AppFullName>()`（lambda 内不能裸 `New`：P0-1 实证 BC30035）。
- `XamlOptionalChanges`/`XamlChangeId` 走 `VB_CodeGenerator.Globalize`；数值 change id 用 `CType`。
- `FieldDefinition` 两构造器 VB 槽位：`internal`→`Friend`，其余首字母大写；默认槽位 VB→`Private`。
- UnhandledException 调试挂钩保留不动；`Sub Program` 删除。
