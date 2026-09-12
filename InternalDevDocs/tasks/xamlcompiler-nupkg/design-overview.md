# 设计概览：仅编译器 nupkg

把 fork 的 XAML 编译器二进制打成一个不含 targets / genxbf / 引用程序集的 nupkg，依赖原厂 targets 的空值守卫与 NuGet props 的导入时机，只替换三个工具路径属性。

## 只覆盖三个属性

| 属性 | 覆盖为 | 不动的原因 |
|------|--------|-----------|
| `XamlCompilerTaskPath` | 本包 `tools\net8.0`(Core) / `tools\net472`(桌面) | — |
| `XamlCompilerJsonTaskPath` | 同目录 `Microsoft.UI.Xaml.Markup.Compiler.IO.dll` | — |
| `XamlCompilerExePath` | 本包 `tools\net472\XamlCompiler.exe` | — |
| `_MuxPackageToolsFolder` | 不动 | 原厂 `interop.targets:24-25` 的 Core 分支无空值守卫；`GenXbfPath`（`:26`）依赖它 |
| `GenXbfPath` | 不动 | `genxbf` 仍由原厂包提供 |
| `XamlCompilerPropsAndTargetsDirectory` | 不动 | 保留原厂 props/targets，只换二进制 |
| `UseXamlCompilerExecutable` | 不设 | 原厂 `:375-376` 在桌面 MSBuild 下硬报错 |

## 导入顺序

`Sdk.props` → `Microsoft.Common.props` → `obj\<proj>.nuget.g.props`（本包 props）→ … → `Sdk.targets` → `Microsoft.WindowsAppSDK.WinUI.targets` → `Microsoft.UI.Xaml.Markup.Compiler.targets:6` → `interop.targets:180-195` 的守卫块。本包赋值在前，守卫失效。

## 载荷

- 目录用 `$(MSBuildThisFileDirectory)..\tools\...`，不硬编码全局包路径。
- 按 `$(MSBuildRuntimeType)` 选 net8.0 / net472。
- `VBWinUI3XamlCompilerEnabled=false` 整体回退原厂编译器。
- `_VBWinUI3XamlCompilerReport`（`BeforeTargets="MarkupCompilePass1"`）打取证消息并检查三个文件存在。
