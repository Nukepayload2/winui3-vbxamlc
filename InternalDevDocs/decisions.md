# 维护决策账本

编号决策的唯一权威。证据阶梯：未提供 / 已提供 / 已检查 / 已运行。

## D1 交付机制 = 仅编译器 nupkg

fork 的 XAML 编译器以仅编译器 nupkg（`Nukepayload2.UI.VBWinUI3.XamlCompiler`，只含 `tools\` 二进制 + 一个 `buildTransitive\` 覆盖载荷）交付，发布在 nuget.org 的 `3.0.0-dev*` 预发布线；本地 feed（`PackageStore`）只用于发布前自测。

理由（已检查）：整包路线要求 `build/nuspecs/Microsoft.WindowsAppSDK.WinUI.nuspec:34-62` 的 `runtimes-framework/**`、`lib/**`、`include/**`，而 `BuildOutput/packaging/Release` 只有 `tools/` 与 `build/`，必须先全量构建整个仓库。

后果：下游需自行保证 `genxbf`、targets、引用程序集仍来自原厂 `Microsoft.WindowsAppSDK`。

## D2 只覆盖三个工具路径属性

只赋值 `XamlCompilerTaskPath` / `XamlCompilerJsonTaskPath` / `XamlCompilerExePath`，不带空值守卫；`_MuxPackageToolsFolder`、`GenXbfPath`、`XamlCompilerPropsAndTargetsDirectory` 不动；不设 `UseXamlCompilerExecutable`。

理由（已检查/已运行）：原厂 `interop.targets:180-195` 三个属性带空值守卫，先赋值者获胜；带守卫会让原厂默认值先落地而覆盖失败。`_MuxPackageToolsFolder` 的 Core 分支无守卫且被 `GenXbfPath`（`:26`）依赖；`UseXamlCompilerExecutable` 在桌面 MSBuild 下为 true 是硬错误（`:375-376`）。导入时机：本包 props 在求值初期导入，守卫却在 targets 阶段（`Microsoft.WinUI.targets:116` → `Microsoft.UI.Xaml.Markup.Compiler.targets:6` → `interop.targets`），任何 props 赋值都先于守卫 —— 与本包 props 在 `nuget.g.props` 中的行序无关，与直接引用还是经别的包传递引用也无关（已运行：传递引用下三属性仍指向本包 `tools\`）。

代价：消费方自设这三个属性会被静默改写。

## D3 包身份与版本

包 id `Nukepayload2.UI.VBWinUI3.XamlCompiler`；工程默认版本 `3.0.0-dev.0`（本地占位，永不发布），发布/迭代一律显式 `-p:PackageVersion=3.0.0-dev.<yyMMdd>.<n>`（数字标识符不带前导零，因此 `3.0.0-dev.260913.001` 非法），稳定线 `3.0.0`；`DevelopmentDependency=true`，MIT，只发 `buildTransitive\`。

理由（已运行）：`~\.nuget\packages\<id>\<ver>` 对 NuGet 不可变，同版本重打被静默忽略；同时发 `build\` 会在直连引用时重复导入（MSB4011）。版本序用 NuGet 自己的比较器核过：`3.0.0-dev.260913.1 > 3.0.0-dev`；单标识符方案（`3.0.0-dev260913001`）不采用——数字位数一变就出现版本倒退（`dev2609130002 < dev260913001`，实测），且 `alpha` 小于 `dev`。

## D4 下游运行时问题不进编译器覆盖面

下游升级 Windows App SDK 遇到的 API 行为变化由消费方处理，不通过改生成器或打包更多原厂文件规避。

理由（已运行）：例如 2.2.0 下 `Window.Title` getter 在窗口显示前抛 `E_FAIL` 导致 `0xC000027B`，该故障在原厂编译器构建下同样复现，与生成器无关。

## D5 下游默认使用生成的入口点

消费方不定义 `DISABLE_XAML_GENERATED_MAIN`、不手写 `Program.vb`：编译器生成的 `Public Module Program` + `Sub Main` 就是入口点（含 ComWrappers 初始化、同步上下文与 `New App()`）。`DISABLE_XAML_GENERATED_MAIN` 保留给需要接管入口点的场景。

理由（已运行）：生成的 `Sub Main` 使工程不再需要 `StartupObject`/手写入口点；`Assembly.EntryPoint` 为 `BatchFfmpegWinUI.Program.Main`，运行验证通过。

代价（已运行）：该示例不能再由原厂编译器构建——原厂 VB 生成器产出 `Public Class Program`，既无 `Sub Main`，也与注入的 `Program` 模块冲突（BC30179）。`-p:VBWinUI3XamlCompilerEnabled=false` 因此只用于比对生成源码。

## D6 被别的 nupkg 依赖时必须 `PrivateAssets="none"`

另一个 nupkg 把本包写成依赖时，该 `PackageReference` 必须带 `PrivateAssets="none"`；应用直接引用本包时不需要。

理由（已运行）：`PrivateAssets` 默认值 `contentfiles;analyzers;build` 会让 NuGet 在 nuspec 依赖上写 `exclude="Build,Analyzers"`，消费方永远拿不到 `buildTransitive\` 里的 props —— 覆盖静默失效、退回原厂编译器且不报任何警告；显式 `none` 后依赖写成 `include="All"`，消费方三属性指向本包 `tools\`。`DevelopmentDependency=true` 不参与该判定：实测打包方照样把本包列为依赖。

代价：包内没有 `lib\`，资产流向全靠这一个属性，写错不报错、只静默退回原厂。
