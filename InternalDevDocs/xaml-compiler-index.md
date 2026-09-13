# XAML 编译器源码索引（轻量）

> 本索引是 WinUI3VBXaml 维护者的源码地图：入口 + 文件地图 + VB 挂钩点 + 现状基线。引用编译器源码一律先查本索引，不凭记忆猜目录。
> 深入架构说明见仓库 `docs/design-notes/xamlcompiler.md`（微软官方架构笔记，含设计动机）。

## 定位

- 本仓库 = `winui3-vbxamlc`（microsoft-ui-xaml 的 fork）。目标：接手并做完微软半成品的 WinUI3 VB XAML 编译器。
- 编译器主体在 `src/XamlCompiler/`。
- 支持的生成语言：C#、VB、C++/CX、C++/WinRT（`Language.cs` 四语言表）。

## 入口与编译管线

| 层 | 文件 | 说明 |
|----|------|------|
| MSBuild Task | `src/XamlCompiler/BuildTasks/CompileXaml.cs` | MSBuild Task 外层，参数 → `CompileXamlInternal` |
| 独立 exe | `src/XamlCompiler/Exe/` | .NET Core MSBuild 用，JSON 输入输出（`XamlCompiler.exe` + `Microsoft.UI.Xaml.Markup.Compiler.IO.dll` 包装） |
| 共享内层 | `src/XamlCompiler/BuildTasks/CompileXamlInternal.cs` | 主流程 `DoExecute`；两 pass 调度、代码生成、XBF 生成 |

`CompileXamlInternal.DoExecute` 主流程：校验参数 → 判断是否需要工作 → 建 TypeUniverse / SchemaContext / TypeResolver → 收集 XAML 类型 → 校验 XAML → 生成代码（pass1/pass2）→ 重写 XAML → 生成 XBF → 生成绑定信息 → 生成 XamlTypeInfo。

## 两 pass 机制

- Pass1：本地类型尚未编译，生成最小 partial 代码让项目能编译。输出 `.g.i.<ext>`。
- Pass2：本地类型已编译（有 winmd/dll），生成完整代码。输出 `.g.<ext>`。
- VB 扩展名：pass1 `.g.i.vb`、pass2 `.g.vb`（`Language.cs`）。

## 代码生成架构

- `Language.cs`：语言表，每语言一组生成器委托（App / Page / TypeInfo / BindingInfo × pass1/pass2），`null` = 该语言不用。
- `XamlCodeGenerator.cs`：按 `IsApplication` + `IsPass1` 派发到对应生成器，调 `TransformText()` 产出代码。
- T4 基类层级（`CodeGenerators/CodeGenerator.cs` 等）：
  `T4Base<T>` → `CodeGenerator<T>` → `ManagedCodeGenerator<T>` → `CSharp_CodeGenerator<T>` / `VB_CodeGenerator<T>`
  （原生侧 → `CppWinRT_CodeGenerator<T>` / `CppCX_CodeGenerator<T>`）
- 语言适配机制（贯穿模型层，不只生成器）：
  - `LanguageSpecificString`：一个字符串带 C#/VB/C++ 各语言委托（`BuildTasks/Utilities/LanguageSpecificString.cs`）
  - `XamlType.VBName()` / `.CSharpName()` 等扩展方法（`TypeForCodeGen.cs`、`XamlSchemaCodeInfo.cs`、`XamlTypeExtensions.cs`）
- 生成器由 `.tt`（T4 模板，源）产出 `.cs`（生成物，提交在库）。**改生成逻辑改 `.tt`，`.cs` 用 T4 工具重新生成**。

## 生成器对照表（`CodeGenerators/`）

| 语言 | AppPass1 | PagePass1 | PagePass2 | TypeInfoPass2 | 语言基类 |
|------|----------|-----------|-----------|---------------|----------|
| C# | `CSharpAppPass1` | `CSharpPagePass1` | `CSharpPagePass2` | `CSharpTypeInfoPass2` | `CSharp_CodeGenerator<T>` |
| **VB** | **`VisualBasicAppPass1`** | **`VisualBasicPagePass1`** | **`VisualBasicPagePass2`** | **`VisualBasicTypeInfoPass2`** | **`VB_CodeGenerator<T>`** |
| C++/CX | `MoComCppAppPass1` | `MoComCppPagePass1` | `MoComCppPagePass2` | `MoComCppTypeInfoPass2` | `CppCX_CodeGenerator<T>` |
| C++/WinRT | `CppWinRT_AppPass1` | `CppWinRT_PagePass1` | `CppWinRT_PagePass2` | `CppWinRT_TypeInfoPass2` | `CppWinRT_CodeGenerator<T>` |

每对 `.cs` / `.tt` 并列在 `CodeGenerators/` 下。C++ 侧还有 BindingInfoPass1/2、XamlMetaDataProvider、TypeInfoPass1/Impl 等生成器，managed 侧（C#/VB）为 `null`（`Language.cs` 表）。

## VB 挂钩点（重点）

| 位置 | 说明 |
|------|------|
| `Language.cs:120-132` | VB 语言条目：扩展名 `.g.i.vb` / `.g.vb`、4 个 VB 生成器 |
| `CodeGenerators/VB_CodeGenerator.cs` | VB 专用 T4 helper：`Globalize()`（`Global.` 前缀）、`ToStringWithCulture`、代码特性、phase 条件（VB 位运算 `And` / `<> 0`） |
| `TypeForCodeGen.cs` / `XamlSchemaCodeInfo.cs` / `XamlTypeExtensions.cs` | `VBName()` 等语言扩展（泛型 `Of`、`(` / `)` 括号等 VB 语法） |
| `BuildTasks/Utilities/LanguageSpecificString.cs` | 每语言字符串委托，`VBName` 是 VB 变体 |
| `CompileXaml.cs:264` | **VB 专用 workaround**：VB 在 pass1 有 TaskFileManager、pass2 没有，非 design-time 构建禁用 TaskFileManager |
| `CompileXamlInternal.cs:2472` | `x:Property` 默认值字面量化用 CodeDomProvider，VB 用 `"vb"` provider |

## 解析层

- `BuildTasks/System.Xaml/`：自带 System.Xaml 副本（OS 版不支持条件命名空间，微软 fork 的，一直沿用）。
- `Microsoft.UI.Xaml.Markup.Compiler.Parsing/`：Antlr 解析（x:Bind `BindingPath.g4`、条件命名空间 `ConditionalNamespace`）。
- 字符串→对象转换：`CreateFromString` 方法 / `XamlBindingHelper.ConvertValue`（核心类型落到属性系统 `CValue`）。

## XBF 生成

- `XbfGenerator`（`CompileXamlInternal` 调用）→ `dxaml/xcp/tools/GenXbfDLL/GenXbf.vcxproj`（genxbf.dll）。
- 按 `TargetPlatformVersion` 选 genxbf 版本；错误回传为 WMC0605。

## MSBuild 集成

- `src/XamlCompiler/Targets/Microsoft.UI.Xaml.Markup.Compiler.interop.targets`：WinUI NuGet 自动导入，配置项目调编译器。
- 语言传递：`XamlLanguage=$(XamlLanguage)`（默认 `$(Language)`）、`LanguageSourceExtension=$(DefaultLanguageSourceExtension)`。
- 包形式：`Microsoft.UI.Xaml.Markup.Compiler.dll`（.NET Framework Task）+ `XamlCompiler.exe`（.NET Core 桥）；**可分发**：`build/xamlcompiler-nupkg/` 产出仅编译器 nupkg（id `Nukepayload2.UI.VBWinUI3.XamlCompiler`），发布在 nuget.org 的 `3.0.0-dev*` 预发布线，引用后即完成三属性覆盖（被别的 nupkg 依赖时须 `PrivateAssets="none"`，见 `decisions.md` D6），见 `tasks/xamlcompiler-nupkg/`。

## 测试与验收

| 层 | 位置 | 现状 |
|----|------|------|
| 单元测试 | `Tests/UnitTests/`（`XamlCompilerUnitTests.csproj`） | `CodeGeneratorTests` 遍历 C#/VB 跑 pass1/pass2（只断言文件数）；设计文档自述 unit test projects currently broken |
| 回归项目 | `Tests/RegressionProjects/` | C#/C++ 在；**VB 项目被移除**（`Basic/VisualBasic` 只剩 sln；`Basic/References` 无 VBExe/VBLib/VBWinrtComponent） |
| 代码比对 | `TestMasters/`（150 个 `.vb` master） | VB master **过时**：UWP 命名空间（`Windows.UI.Xaml`、`Microsoft.Windows.UI.Xaml.Build.Tasks`），当前生成器输出 WinUI3（`Microsoft.UI.Xaml`） |
| 运行脚本 | `runtests.cmd` | 详见 `docs/design-notes/xamlcompiler.md`「Regression and Unit Tests」 |

**验收标准（接手后）**：VB 生成代码过 vbc 编译 + 跑通 WinUI3 运行时；VB 回归项目重建 + master 用 WinUI3 命名空间重生成；C#/VB 遍历测试全部通过。

## VB 现状基线（半成品，接手起点）

| 项 | 状态（证据） |
|----|------|
| VB 生成器 | 已接线且方法面完整（已检查 `Language.cs`、`VisualBasicPagePass2.cs`） |
| VB 冒烟测试 | 在，但只断言文件数（已检查 `CodeGeneratorTests.cs`） |
| VB 回归测试 | 全部 `[Ignore]`，项目已移除（已检查 `CodegenTests.cs` 与 `Tests/RegressionProjects/` 目录） |
| VB test masters | 过时（UWP 命名空间），未随 WinUI3 迁移（已检查 `TestMasters/.../MainPage.g.vb`） |
| 端到端验证 | 已完成（已运行）：demo 仓库 `VbWinUI3Demos/BatchFfmpegWinUI` 经 submodule 本地 feed 引用仅编译器 nupkg，`dotnet build`（Core/Exec）与桌面 MSBuild（VS，in-proc）两条路径均构建 0 错误、窗口运行通过；对原厂编译器的差分构建仅入口点块与版本串有差异。详见 `tasks/xamlcompiler-nupkg/test-plan.md` |
| 已知 VB 特殊代码 | `CompileXaml.cs:264` TaskFileManager workaround、`CompileXamlInternal.cs:2472` CodeDom `"vb"`（已检查） |

> 「微软做了一半」的具体含义 = 生成器写了大半且随 C# 同步，但回归基础设施（项目 + master）停在 UWP 时代且被剥离，端到端从未验证。接手目标 = 修好生成器输出可编译可运行的 VB + 重建回归测试与 master + 端到端验证 + 维持上游合并。

## 维护纪律

- 引用编译器源码一律 `文件:行号` 锚点，不凭记忆。
- **改 C# 生成器必须同步改 VB 生成器**（微软在同步，本 fork 继续同步）。
- `.tt` 是源，`.cs` 是生成物；两者要保持同步，改完 `.tt` 用 T4 工具重新生成。
- 上游合并见 `upstream-merge.md`。
