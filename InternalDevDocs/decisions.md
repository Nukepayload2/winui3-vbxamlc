# 维护决策账本

编号决策的唯一权威。证据阶梯：未提供 / 已提供 / 已检查 / 已运行。

## D1 交付机制 = 仅编译器 nupkg

fork 的 XAML 编译器以仅编译器 nupkg（`Nukepayload2.UI.VBWinUI3.XamlCompiler`，只含 `tools\` 二进制 + 一个 `buildTransitive\` 覆盖载荷）交付，面向本地 feed，不发布 nuget.org。

理由（已检查）：整包路线要求 `build/nuspecs/Microsoft.WindowsAppSDK.WinUI.nuspec:34-62` 的 `runtimes-framework/**`、`lib/**`、`include/**`，而 `BuildOutput/packaging/Release` 只有 `tools/` 与 `build/`，必须先全量构建整个仓库。

后果：下游需自行保证 `genxbf`、targets、引用程序集仍来自原厂 `Microsoft.WindowsAppSDK`。

## D2 只覆盖三个工具路径属性

只赋值 `XamlCompilerTaskPath` / `XamlCompilerJsonTaskPath` / `XamlCompilerExePath`，不带空值守卫；`_MuxPackageToolsFolder`、`GenXbfPath`、`XamlCompilerPropsAndTargetsDirectory` 不动；不设 `UseXamlCompilerExecutable`。

理由（已检查/已运行）：原厂 `interop.targets:180-195` 三个属性带空值守卫，先赋值者获胜；带守卫会让原厂默认值先落地而覆盖失败。`_MuxPackageToolsFolder` 的 Core 分支无守卫且被 `GenXbfPath`（`:26`）依赖；`UseXamlCompilerExecutable` 在桌面 MSBuild 下为 true 是硬错误（`:375-376`）。导入时机：`obj\*.nuget.g.props:17` 早于 `:23`。

代价：消费方自设这三个属性会被静默改写。

## D3 包身份与版本

包 id `Nukepayload2.UI.VBWinUI3.XamlCompiler`，默认版本 `3.0.0-dev`（与 `Build.cmd:28`、`pack.component.cmd:9` 一致），`DevelopmentDependency=true`，MIT，只发 `buildTransitive\`。

理由（已运行）：`~\.nuget\packages\<id>\<ver>` 对 NuGet 不可变，同版本重打被静默忽略；同时发 `build\` 会在直连引用时重复导入（MSB4011）。

## D4 下游运行时问题不进编译器覆盖面

下游升级 Windows App SDK 遇到的 API 行为变化由消费方处理，不通过改生成器或打包更多原厂文件规避。

理由（已运行）：例如 2.2.0 下 `Window.Title` getter 在窗口显示前抛 `E_FAIL` 导致 `0xC000027B`，该故障在原厂编译器构建下同样复现，与生成器无关。
