# 任务：仅编译器 nupkg 交付

- **阶段**：已完成
- **源码地图**：`InternalDevDocs/xaml-compiler-index.md`
- **消费方**：`C:\Users\james\Projects\VbWinUI3Demos`（`BatchFfmpegWinUI`）

## 目标

把本 fork 的 XAML 编译器二进制打成仅编译器 nupkg（`Nukepayload2.UI.VBWinUI3.XamlCompiler`），下游经本地 feed 引用后由其执行 XAML 编译。不发 nuget.org，不打完整 WinUI 包。

## 交付物

| 路径 | 说明 |
|------|------|
| `build/xamlcompiler-nupkg/XamlCompilerPackage.vbproj` | 打包工程（VB，配影子 Directory.Build.props/targets） |
| `build/xamlcompiler-nupkg/Nukepayload2.UI.VBWinUI3.XamlCompiler.props` | 覆盖载荷，随包发到 `buildTransitive\` |
| `build/xamlcompiler-nupkg/pack.cmd` | `dotnet pack -c Release -o ..\..\PackageStore` |
| `build/xamlcompiler-nupkg/readme.md` | 随包分发的使用说明 |
| `PackageStore/Nukepayload2.UI.VBWinUI3.XamlCompiler.3.0.0-dev.nupkg` | 产物，`PackageStore` 为 git-ignored |

## 判据与结果（已运行）

| 项 | 判据 | 结果 |
|----|------|------|
| 布局 | `net8.0`=7、`net472`=19、`NOTICE.txt`；dll mtime 晚于 `VisualBasicAppPass1.tt` | 满足；dll FileVersion `3.0.0.0`（原厂 `3.0.0.2606`） |
| nupkg | 条目与排除项见 `test-plan.md` | 满足 |
| 导入顺序 | 本包 props 的导入行早于原厂 WinUI props | `obj\*.nuget.g.props:17` < `:23` |
| 覆盖生效 | 日志含 `VBWinUI3 XamlCompiler override active:`，三路径在本包 `tools\` 下 | 满足 |
| 编译 | `/define` 无空条目、无 BC31030；`DISABLE_XAML_GENERATED_MAIN` 未定义 | 满足（28 项，该常量 0 次） |
| 生成物 | 活动入口点 `Public Module Program` / `Sub Main` 与未编译的 DISABLE 钩子文本齐备；戳 `3.0.0.0`，无 `3.0.0.2606` | 满足 |
| 差分 | 与原厂编译器的差异仅入口点块与版本串 | 满足 |
| 运行 | 窗口出现并稳定存活 | 满足 |
| 桌面 MSBuild（VS） | 构建 + 运行通过；覆盖走 net472、无 `input.json`/`output.json`（in-proc 任务） | 满足（见 `test-plan.md` E10） |

依据（已检查）：原厂 `interop.targets:180-195` 三个工具路径属性的空值守卫；`eng/usexamlcompiler.props:7-14`（Core 下指向 `..\tools\net8.0\`）与 `Samples/DisableXamlGeneratedMain/Vb/Vb.vbproj:35-37` 的同类路径覆盖先例。

## 已决事项

1. 交付机制 = 仅编译器 nupkg。整包路线要求 `build/nuspecs/Microsoft.WindowsAppSDK.WinUI.nuspec:34-62` 的 `runtimes-framework/**`、`lib/**`、`include/**`，需先全量构建整个仓库，排除。
2. 只覆盖 `XamlCompilerTaskPath` / `XamlCompilerJsonTaskPath` / `XamlCompilerExePath`，不带空值守卫；`_MuxPackageToolsFolder`、`GenXbfPath`、`XamlCompilerPropsAndTargetsDirectory` 不动；不设 `UseXamlCompilerExecutable`（`interop.targets:375-376`）。
3. 只发 `buildTransitive\`；同时发 `build\` 会在直连引用时重复导入（MSB4011）。
4. 包 id `Nukepayload2.UI.VBWinUI3.XamlCompiler`，默认版本 `3.0.0-dev`，可用 `-p:PackageVersion=` 覆盖。
5. 下游默认使用生成的入口点：不定义 `DISABLE_XAML_GENERATED_MAIN`、不手写 `Program.vb`。代价是该示例不能再用原厂编译器构建（BC30179）。

## 未覆盖

- 桌面 MSBuild 路径未做生成源码差分比对（只验证了构建 + 运行）。
- demo 的 `ProgressDialog.xaml` 未交互式打开。
- 未验证手写入口点路径（定义 `DISABLE_XAML_GENERATED_MAIN` + 调用 `XamlGeneratedProgram.XamlGeneratedMain()`）；该路径由 fork 样本 `Samples/DisableXamlGeneratedMain/{Vb,VbNoCtor}` 覆盖。
