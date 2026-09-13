# 详细设计：仅编译器 nupkg

## 1. 打包工程（`build/xamlcompiler-nupkg/`）

| 文件 | 作用 |
|------|------|
| `XamlCompilerPackage.vbproj` | 打包工程：VB、`netstandard2.0`、`IncludeBuildOutput=false`、`SuppressDependenciesWhenPacking=true`。载荷与语言无关（进包的只有 `Content` 项，产物程序集丢弃） |
| `Directory.Build.props` / `Directory.Build.targets` | 影子文件（空 `<Project>`）：MSBuild 只导入向上找到的第一个，用来挡住仓库根那份（`IsInWinUIRepo`、`eng\*.props`、`binplace`/`packaging`/`projectcaching` 链路） |
| `Nukepayload2.UI.VBWinUI3.XamlCompiler.props` | 覆盖载荷 |
| `Placeholder.vb` | 占位编译单元 |
| `readme.md` | 随包分发的使用说明 |
| `pack.cmd` | `dotnet pack -c Release -o ..\..\PackageStore`，参数透传 |

### vbproj 要点

- `XamlCompilerLayoutConfiguration` 默认 `Release`，与 `$(Configuration)` 解耦。
- `XamlCompilerToolsLayout` = `..\..\BuildOutput\packaging\$(XamlCompilerLayoutConfiguration)\tools\`。
- `Content` 三条：`net8.0\**`、`net472\**` → `tools\...`（`%(RecursiveDir)%(Filename)%(Extension)`），`NOTICE.txt` → `tools\`。
- `_VerifyXamlCompilerLayout`（`BeforeTargets="GenerateNuspec"`）在布局缺失时早失败。
- `NoWarn` 加 `NU5100;NU5128`（tools 下放程序集、无 lib/ref 组，属本包形态）。

### 包布局

```
tools/net8.0/   7 个文件
tools/net472/  19 个文件
tools/NOTICE.txt
buildTransitive/Nukepayload2.UI.VBWinUI3.XamlCompiler.props
readme.md
Nukepayload2.UI.VBWinUI3.XamlCompiler.nuspec   （无 <dependencies>）
```

不含 `build/`、`lib/`、`include/`、`runtimes-framework/`、`GenXbf`。

## 2. 覆盖载荷

- `_VBWinUI3XamlCompilerToolsDir`：默认 `..\tools\net472\`，`$(MSBuildRuntimeType) == 'Core'` 时 `..\tools\net8.0\`（与原厂 `interop.targets:24-25` 同构，目录名不同）。
- 三个属性在同一 `PropertyGroup` 内无条件赋值：带空值守卫会让原厂默认值先落地，覆盖失败。代价是消费方若自设这三个属性会被静默改写。
- `VBWinUI3XamlCompilerEnabled=false`：整个 `PropertyGroup` 跳过，原厂默认值生效，`_VBWinUI3XamlCompilerReport` 也跳过。
- 只放 `buildTransitive\`：直连 `PackageReference` 时 NuGet 会同时导入 `build\` 与 `buildTransitive\`，两处都放触发 MSB4011。

## 3. 与原厂的差异

- Core 工具目录名 `net8.0`（原厂 `net6.0`），文件集合逐名相同（7 / 19）。

非差异（仅记录，不进上面的差异清单）：fork 的 `interop.targets` 另多 `EnabledXamlOptionalChanges` / `DisabledXamlOptionalChanges` 透传（原厂那份 0 处引用），本包不发该文件，两属性保持空值默认，生成结果与原厂 targets 一致。该机制与语言无关：VB 模板 `VisualBasicAppPass1.tt` 的 `WriteCommonInit` 同样输出，被 `:28`（非 DISABLE）与 `:49`（DISABLE）两处入口调用。fork 参数面是原厂严格超集，多传未知参数会硬报 `MSB4064`。

升级 WinUI 包后复查：

```
$o="$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk.winui\<ver>\buildTransitive\Microsoft.UI.Xaml.Markup.Compiler.interop.targets"
$f="src\XamlCompiler\Targets\Microsoft.UI.Xaml.Markup.Compiler.interop.targets"
Compare-Object (Get-Content $o) (Get-Content $f)
```

## 4. 构建与打包

```
msbuild XamlCompilerPrerequisites.sln /p:Configuration=Release /p:Platform=x64 /restore /m
build\xamlcompiler-nupkg\pack.cmd
```

Core MSBuild（`dotnet build`）**不能**构建这个 sln：两个受管工程无条件 import VS 的 `$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\TextTemplating\Microsoft.TextTemplating.targets`（`src/XamlCompiler/Microsoft.UI.Xaml.Markup.Compiler.csproj:526`、`Exe/Microsoft.UI.Xaml.Markup.Compiler.Executable.csproj:506`），且图里含 `GenXbf.vcxproj`（需要 `$(VCTargetsPath)`）；三条都是 MSB4278。可行的 dotnet 路径 = 只构建受管工程 + 指路：

```
dotnet build src\XamlCompiler\Microsoft.UI.Xaml.Markup.Compiler.csproj -c Release -p:Platform=x64 ^
  -p:MSBuildExtensionsPath="<VS>\MSBuild" -p:VisualStudioVersion=18.0
```

已运行：布局 `net8.0`=7 / `net472`=19、零错误。该布局打出的包与 msbuild 版文件大小相同、`ProductVersion` 相同（`3.0.0.0+<commit>`），字节不同源于两套 Roslyn；用该包构建 fork 样本 `Samples/DisableXamlGeneratedMain/Vb` 并运行通过。前提仍是装有 VS（T4 targets 由 VS 提供）。

前置条件：

- `eng/projectcaching.props:20` 无条件导入 `packages\$(MSBuildCachePackageName).$(ver)\build\*.props`（版本取自 `packages.config`）。该包必须以 `Id.Version` 布局存在于 `packages\`，否则求值阶段 MSB4019。
- 该 sln 还包含与本包无关的 C++ 工程（`dxaml/xcp/dxaml/manifest`、`eng/genmrtheadersandidl`、`eng/gencompheadersandidl`），缺 `Microsoft.Windows.SDK.cpp` 时会报错；编译器布局在这些工程之前已产出。
- `.tt` 是源、`.cs` 是提交在库的生成物：布局新鲜度按「布局 dll mtime 晚于对应 `.tt`」核对。

## 5. 下游接线

1. `PackageReference`：`Microsoft.WindowsAppSDK` 2.2.0（WinUI 子包解析为 2.2.1）+ `Nukepayload2.UI.VBWinUI3.XamlCompiler` 3.0.0-dev.260913.1。不加 `PrivateAssets`/`ExcludeAssets`（会连 `buildTransitive` 导入一起掐掉）。
2. 仓库根 `nuget.config`：加本地源 `vbxamlc\PackageStore`（demo 以 submodule 形式引入本仓库，路径 `vbxamlc`），不写 `<clear/>`。submodule 内的 `PackageStore` 是 git-ignored，clone 后需先打包；submodule 不在该位置时用 `dotnet restore -p:RestoreAdditionalProjectSources=<绝对路径>`，不要用裸 `--source`（会替换全部源）。
3. 入口点由编译器生成（`Public Module Program` + `Sub Main`），不需要 `Program.vb`。只有要手写入口点时才定义 `DISABLE_XAML_GENERATED_MAIN` 并调用生成的 `XamlGeneratedProgram.XamlGeneratedMain()`。追加 `DefineConstants` 时用快照写法：直接写 `$(DefineConstants),X` 会产生前导逗号（`FinalDefineConstants` 空常量名 → BC31030），两行同测 `$(DefineConstants)` 则会把常量加两次。

## 6. 排查

| 症状 | 原因 | 处置 |
|------|------|------|
| 换了编译器但生成代码没变 | XAML 编译的跳过判定不含编译器二进制 | 删 `obj\`、`bin\` |
| 重打包后行为没变 | `~\.nuget\packages\<id>\<ver>` 对 NuGet 不可变 | 递增版本或删缓存目录再 restore |
| `MSB4019` 缺 `Microsoft.MSBuildCache.Local*.props` | 构建前置包不在 `packages\` | 见第 4 节 |
| `MSB1025 ... MSBuildTemp*` | 临时目录不可写 | 把 `TEMP`/`TMP` 指到可写目录 |
| 构建成功但运行崩 `0xC000027B` | WinRT/XAML 运行时问题，与编译器无关（本仓库触发点为 `Window.Title` 在窗口显示前被读取） | 按 WinUI 自身排查 |
