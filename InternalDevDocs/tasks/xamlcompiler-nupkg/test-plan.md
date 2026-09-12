# 测试计划与结果：仅编译器 nupkg

执行环境：Windows；VS 18 的 MSBuild（Release 构建编译器）、.NET SDK 10.0.400（demo 构建）、Windows App SDK 2.2.0（WinUI 子包 2.2.1）。

## E2 布局

命令：`msbuild XamlCompilerPrerequisites.sln /p:Configuration=Release /p:Platform=x64 /restore /m`

| 判据 | 结果 |
|------|------|
| `net8.0` = 7、`net472` = 19、`NOTICE.txt` 存在 | 满足 |
| `net8.0\Microsoft.UI.Xaml.Markup.Compiler.dll` mtime 晚于 `CodeGenerators\VisualBasicAppPass1.tt` | 2026-09-12 17:24:28 > 2026-08-30 20:07:52 |
| dll FileVersion = `3.0.0.0` | 满足（原厂 2.2.1 为 `3.0.0.2606`） |

## E3 nupkg 清单

| 判据 | 结果 |
|------|------|
| `tools/net8.0/`(7) + `tools/net472/`(19) + `tools/NOTICE.txt` | 满足 |
| `buildTransitive/Nukepayload2.UI.VBWinUI3.XamlCompiler.props`、`readme.md`、包元数据 | 满足 |
| nuspec 无 `<dependencies>` | 满足 |
| 无 `lib/`、`build/`、`runtimes-framework/`、`include/` | 满足 |

## E5 restore

| 判据 | 结果 |
|------|------|
| `obj\BatchFfmpegWinUI.vbproj.nuget.g.props` 含本包 props 的 Import | 第 17 行 |
| 该行早于原厂 `Microsoft.WindowsAppSDK.WinUI.props` 的 Import 行 | 17 < 23 |
| `project.assets.json` 解析到 `Nukepayload2.UI.VBWinUI3.XamlCompiler/3.0.0-dev` | 命中 |
| 还原源含本地 `vbwinui3\PackageStore` | 命中 |

## E6 构建

命令：`dotnet build BatchFfmpegWinUI\BatchFfmpegWinUI.vbproj -c Debug -p:Platform=x64 -t:Rebuild -v:diag`

| 判据 | 结果 |
|------|------|
| 日志含 `VBWinUI3 XamlCompiler override active:`，三路径在本包 `tools\` 下 | 满足 |
| `/define` 中 `DISABLE_XAML_GENERATED_MAIN` 一次、无空条目 | 29 个条目，该常量 1 次，空条目 0 |
| 无 BC31030 | 0 次 |

## E7 fork 专属标记

生成目录：`BatchFfmpegWinUI\obj\x64\Debug\net10.0-windows10.0.19041.0\`。

| # | 标记 | 结果 |
|---|------|------|
| 1 | `input.json` 与 `output.json` 同时存在 | 均在（102036 / 17795 字节） |
| 2 | `App.g.i.vb` 含 `Friend Module XamlGeneratedProgram`、`Sub XamlGeneratedMain()`、`Friend Shared Sub XamlGeneratedCreateApplicationInstance()` | 第 37 / 41 / 66 行 |
| 3 | `App.g.vb` 含 `Private Shared Sub _XamlGeneratedCreateApplicationInstance()`、`Dim _application As New App()` | 第 18 / 19 行 |
| 4 | 生成文件戳 `" 3.0.0.0"`，无 `3.0.0.2606` | 命中（App.g.i 6、MainWindow.g.i 23、XamlTypeInfo 10 …） |

产物 `BatchFfmpegWinUI.dll` 上 `findstr /m /c:XamlGeneratedProgram` 命中。

## E8 差分构建

两次都清空 `obj`/`bin`：默认（fork）构建与 `-p:VBWinUI3XamlCompilerEnabled=false` 对照，逐文件 `Compare-Object`。

| 文件 | 差异行数 | 差异内容 |
|------|---------|---------|
| `App.g.i.vb` | 53 | 入口点块（fork 的 `#If DISABLE_XAML_GENERATED_MAIN` 钩子 + `Public Module Program`；原厂的 `Public Class Program` + `Sub Program` + `<MTAThread()>`）+ 版本串 |
| `App.g.vb` | 5 | fork 的 DISABLE 契约块 |
| `MainWindow.g.i.vb` | 46 | 23 处版本串 |
| `MainWindow.g.vb` | 4 | 2 处版本串 |
| `ProgressDialog.g.i.vb` | 10 | 5 处版本串 |
| `ProgressDialog.g.vb` | 4 | 2 处版本串 |
| `XamlTypeInfo.g.vb` | 20 | 10 处版本串，其余逐字节相同 |

对照侧日志无覆盖消息、生成物戳 `3.0.0.2606`，确认两侧使用了不同编译器。

## E9 运行

| 判据 | 结果 |
|------|------|
| 启动后 24s 内不退出 | 存活 >24s |
| `MainWindowHandle != 0` | 4594744 |
| 标题非空 | `WinUI 3 VB Demo - mp4 converter` |
| 输出含三个 xbf | `App.xbf` 726 / `MainWindow.xbf` 9039 / `ProgressDialog.xbf` 1233 |
| 运行的是 fork 产物 | obj 生成物戳 `3.0.0.0`，产物 dll 含 `XamlGeneratedProgram` |

## 未覆盖

- 桌面 MSBuild / Visual Studio 路径（in-proc `CompileXaml`、net472 分支）。
- demo 的 `ProgressDialog.xaml` 未交互式打开。
- demo 的 `Program.Main` 不调用 `XamlGeneratedProgram.XamlGeneratedMain()`，运行验证不覆盖新挂钩语义。
