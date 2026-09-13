# Nukepayload2.UI.VBWinUI3.XamlCompiler

A compiler-only replacement for the XAML compiler binaries that ship inside
`Microsoft.WindowsAppSDK.WinUI`. Built from
[winui3-vbxamlc](https://github.com/Nukepayload2/winui3-vbxamlc), a fork of `microsoft-ui-xaml`
whose Visual Basic code generators are fixed: with `DISABLE_XAML_GENERATED_MAIN` defined they emit
`XamlGeneratedProgram.XamlGeneratedMain()` plus the two-layer `XamlGeneratedCreateApplicationInstance`
hooks that the stock compiler does not produce for VB.

The package contains only the compiler binaries. The MSBuild targets, `genxbf` and the reference
assemblies keep coming from `Microsoft.WindowsAppSDK`; three properties are redirected:

| Property | Redirected to |
| --- | --- |
| `XamlCompilerTaskPath` | `tools\net472\Microsoft.UI.Xaml.Markup.Compiler.dll` (desktop MSBuild) / `tools\net8.0\…` (Core MSBuild) |
| `XamlCompilerJsonTaskPath` | `tools\net472\Microsoft.UI.Xaml.Markup.Compiler.IO.dll` (desktop) / `tools\net8.0\…` (Core) |
| `XamlCompilerExePath` | `tools\net472\XamlCompiler.exe` |

NuGet imports the props through `obj\<project>.nuget.g.props`, which is evaluated before the
empty-value guards in the stock `Microsoft.UI.Xaml.Markup.Compiler.interop.targets`, so these
assignments win.

## Usage

```xml
<ItemGroup>
  <PackageReference Include="Nukepayload2.UI.VBWinUI3.XamlCompiler" Version="3.0.0-dev.260913.1" />
</ItemGroup>
```

`DevelopmentDependency=true` keeps the package private, so no `PrivateAssets` / `ExcludeAssets` are
needed - adding them would also suppress the `buildTransitive` import that does the override.

To use the stock compiler instead:

```
dotnet build -p:VBWinUI3XamlCompilerEnabled=false
```

## Verifying which compiler ran

1. The build log contains `VBWinUI3 XamlCompiler override active: …` with all three paths under
   `~\.nuget\packages\nukepayload2.ui.vbwinui3.xamlcompiler\<version>\tools\`.
2. The generated `App.g.i.vb` contains `Friend Module XamlGeneratedProgram` and
   `Sub XamlGeneratedMain()`.
3. Every generated file carries
   `GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.0")`; the stock 2.2.x
   compiler stamps `3.0.0.2606`.

Incremental builds decide whether to re-run XAML compilation from a fingerprint of the XAML files,
references and local assemblies, not of the compiler binaries: delete `obj\` and `bin\` after
switching or repacking the compiler.

## Repacking

```
:: 1. build the compiler in Release (writes BuildOutput\packaging\Release\tools)
Build.cmd product

:: 2. pack into ..\..\PackageStore
build\xamlcompiler-nupkg\pack.cmd
```

`~\.nuget\packages\<id>\<version>` is immutable to NuGet, so a repacked version is ignored in favour
of the cached copy. Bump the version (`pack.cmd -p:PackageVersion=3.0.0-dev.260913.2`) or delete the
cached folder and restore again.

Releases use the pre-release line `3.0.0-dev.<yyMMdd>.<n>` (numbers without leading zeros, so
`3.0.0-dev.260913.1` is valid and `3.0.0-dev.260913.001` is not); the stable line will be `3.0.0`.

## Differences from the stock tools

- The Core-MSBuild tool folder is `tools\net8.0\` instead of `tools\net6.0\`; the file names are the
  same.
