// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// VB generator feature-parity tests.
//
// L1 (string assertions): verify the VB code-behind generator produces the
// shapes promised by the P1/P2 implementation:
//   - x:FieldModifier internal -> Friend (not the old ToTitleCase "Internal")
//   - App entry point as "Public Module Program" with Sub Main (no Shared,
//     no thread-model attribute, ComWrappers init inlined, XamlOptionalChanges
//     with Globalized/CType forms, DISABLE two-layer hooks).
// L2 (vbc/csc compile gate): feed the generated code to the Roslyn compilers
// shipped with the .NET SDK and assert zero errors.  These are compiler-intrinsic
// validations that run inside the test infrastructure (allowed by the task
// discipline).  They are gated on local reference assemblies being present so a
// machine without the WinUI NuGet cache skips them (Assert.Inconclusive) instead
// of hitting the network.
//
// No generator sources are modified by these tests.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win8Xaml.CompilerProxies;

namespace UnitTests
{
    [TestClass]
    public class VbFeatureParityTests
    {
        TestHelper _testHelper;

        [TestInitialize]
        public void SchemaInit()
        {
            _testHelper = new TestHelper();
        }

        // ===== XAML inputs =====

        // {0} is the optional x:FieldModifier attribute text.
        const string PageXamlTemplate = @"
<Page
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyClass'>
    <Grid>
        <Button x:Name='btn'{0} />
    </Grid>
</Page>";

        const string AppXaml = @"
<Application
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyApp'>
</Application>";

        // ===== helpers =====

        private CodeGeneratorProjectContext CreateContext(bool isApplication)
        {
            var context = new CodeGeneratorProjectContext(new Version(KnownVersions.Latest));
            context.IsApplication = isApplication;
            return context;
        }

        // The XamlProjectInfo proxy only surfaces a subset of the internal
        // XamlProjectInfo properties, so reach through to the wrapped instance.
        private static void SetProjectInfo(XamlProjectInfo proxy, string propertyName, object value)
        {
            FieldInfo instanceField = typeof(XamlProjectInfo).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(instanceField, "XamlProjectInfo proxy '_instance' field not found");
            object instance = instanceField.GetValue(proxy);
            PropertyInfo prop = instance.GetType().GetProperty(propertyName);
            Assert.IsNotNull(prop, "internal XamlProjectInfo property '{0}' not found", propertyName);
            prop.SetValue(instance, value, null);
        }

        private static string Combine(List<FileNameAndContentPair> pairs)
        {
            var sb = new StringBuilder();
            if (pairs != null)
            {
                foreach (FileNameAndContentPair p in pairs)
                {
                    if (p != null && p.Contents != null)
                    {
                        sb.AppendLine(p.Contents);
                    }
                }
            }
            return sb.ToString();
        }

        // Generates pass1 + pass2 code-behind for the given language and returns
        // the concatenated text.  Mirrors CodeGeneratorTests.NormalUsage.
        private string Generate(CodeGeneratorProjectContext context, string xaml, CodeGenLanguage lang)
        {
            DirectUISchemaContext schema = _testHelper.LoadSchema(SchemaMode.ManagedRuntime);
            List<string> xamlStrings = new List<string> { xaml };

            context.IsPass1 = true;
            List<FileNameAndContentPair> pass1 = _testHelper.GenerateCodeBehind(context, xamlStrings, schema, lang);
            context.IsPass1 = false;
            List<FileNameAndContentPair> pass2 = _testHelper.GenerateCodeBehind(context, xamlStrings, schema, lang);

            return Combine(pass1) + "\n" + Combine(pass2);
        }

        private string GenerateVB(string xaml, bool isApplication, Action<CodeGeneratorProjectContext> configure = null)
        {
            CodeGeneratorProjectContext context = CreateContext(isApplication);
            if (configure != null)
            {
                configure(context);
            }
            return Generate(context, xaml, CodeGenLanguage.VisualBasic);
        }

        private static string PageXaml(string fieldModifier)
        {
            string attr = String.IsNullOrEmpty(fieldModifier) ? "" : " x:FieldModifier='" + fieldModifier + "'";
            return String.Format(PageXamlTemplate, attr);
        }

        // ===== U1-1 .. U1-3: x:FieldModifier VB keyword mapping =====

        [TestMethod]
        public void U1_1_FieldModifier_Internal_EmitsFriendWithEvents()
        {
            string code = GenerateVB(PageXaml("internal"), isApplication: false);

            Assert.IsTrue(code.Contains("Friend WithEvents btn"),
                "expected 'Friend WithEvents btn' in output:\n" + code);
            Assert.IsFalse(code.Contains("Internal WithEvents"),
                "must not emit the un-compilable 'Internal WithEvents' (ToTitleCase bug):\n" + code);
        }

        [TestMethod]
        public void U1_2_FieldModifier_Public_EmitsPublicWithEvents()
        {
            string code = GenerateVB(PageXaml("public"), isApplication: false);

            Assert.IsTrue(code.Contains("Public WithEvents btn"),
                "expected 'Public WithEvents btn' in output:\n" + code);
        }

        [TestMethod]
        public void U1_3_FieldModifier_Default_EmitsPrivateWithEvents()
        {
            string code = GenerateVB(PageXaml(null), isApplication: false);

            Assert.IsTrue(code.Contains("Private WithEvents btn"),
                "expected default slot 'Private WithEvents btn' in output:\n" + code);
        }

        // ===== U1-4 .. U1-7: App.xaml entry point (Module Program) =====

        [TestMethod]
        public void U1_4_AppEntryPoint_ModuleProgram_InlineInit_NoMTA_NoSubProgram()
        {
            // UsingCSWinRT=true so the ComWrappers init is present and must be
            // inlined inside Sub Main (not in a Sub New).
            string code = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", true);
            });

            Assert.IsTrue(code.Contains("Public Module Program"),
                "expected 'Public Module Program':\n" + code);
            Assert.IsTrue(code.Contains("Sub Main(ByVal args() As String)"),
                "expected Sub Main(ByVal args() As String):\n" + code);
            Assert.IsFalse(code.Contains("Shared Sub Main"),
                "Module member Sub Main must not be written with 'Shared':\n" + code);
            Assert.IsTrue(code.Contains("Global.WinRT.ComWrappersSupport.InitializeComWrappers()"),
                "expected InitializeComWrappers inside Sub Main body:\n" + code);
            Assert.IsFalse(code.Contains("MTAThread"),
                "must not emit <MTAThread()> (would override default STA):\n" + code);
            Assert.IsFalse(code.Contains("Sub Program"),
                "legacy 'Sub Program' must be gone:\n" + code);
        }

        [TestMethod]
        public void U1_5_IsWin32App_NeitherMtaNorStaAttribute()
        {
            foreach (bool isWin32App in new[] { true, false })
            {
                string code = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
                {
                    SetProjectInfo(ctx.ProjectInfo, "IsWin32App", isWin32App);
                });

                Assert.IsFalse(code.Contains("<MTAThread()"), "IsWin32App={0}: no MTAThread attribute expected:\n{1}", isWin32App, code);
                Assert.IsFalse(code.Contains("<STAThread()"), "IsWin32App={0}: no STAThread attribute expected (VB entry points default to STA):\n{1}", isWin32App, code);
            }
        }

        [TestMethod]
        public void U1_6_UsingCSWinRT_ControlsComWrappersAndSyncContext()
        {
            string withCswinrt = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", true);
            });
            Assert.IsTrue(withCswinrt.Contains("Global.WinRT.ComWrappersSupport.InitializeComWrappers()"),
                "UsingCSWinRT=true should emit InitializeComWrappers:\n" + withCswinrt);
            Assert.IsTrue(withCswinrt.Contains("DispatcherQueueSynchronizationContext"),
                "UsingCSWinRT=true should emit DispatcherQueueSynchronizationContext:\n" + withCswinrt);

            string withoutCswinrt = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", false);
            });
            Assert.IsFalse(withoutCswinrt.Contains("InitializeComWrappers"),
                "UsingCSWinRT=false must not emit InitializeComWrappers:\n" + withoutCswinrt);
            Assert.IsFalse(withoutCswinrt.Contains("DispatcherQueueSynchronizationContext"),
                "UsingCSWinRT=false must not emit DispatcherQueueSynchronizationContext:\n" + withoutCswinrt);
        }

        [TestMethod]
        public void U1_7_XamlOptionalChanges_GlobalizedCTypeAndNamedForms()
        {
            string code = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "EnabledXamlOptionalChanges", new List<string> { "5", "MyChange" });
                SetProjectInfo(ctx.ProjectInfo, "DisabledXamlOptionalChanges", new List<string> { "7" });
            });

            Assert.IsTrue(code.Contains("EnableChange(CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId))"),
                "numeric change id must be Globalized via CType:\n" + code);
            Assert.IsTrue(code.Contains("EnableChange(Global.Microsoft.UI.Xaml.Settings.XamlChangeId.MyChange)"),
                "named change id must be Globalized as enum member:\n" + code);
            Assert.IsTrue(code.Contains("DisableChange(CType(7, Global.Microsoft.UI.Xaml.Settings.XamlChangeId))"),
                "disabled change id must emit DisableChange(CType(...)):\n" + code);
        }

        // ===== U1-8: C# side stays unchanged (regression) =====

        [TestMethod]
        public void U1_8_CSharpOutput_StaysLowercase_StaThread_ComWrappers()
        {
            // Page field modifiers: C# must keep lowercase internal/public/private.
            string pageInternal = GenerateCSharp(PageXaml("internal"), isApplication: false);
            Assert.IsTrue(pageInternal.Contains("internal global::"), "C# internal field expected:\n" + pageInternal);

            string pagePublic = GenerateCSharp(PageXaml("public"), isApplication: false);
            Assert.IsTrue(pagePublic.Contains("public global::"), "C# public field expected:\n" + pagePublic);

            string pageDefault = GenerateCSharp(PageXaml(null), isApplication: false);
            Assert.IsTrue(pageDefault.Contains("private global::"), "C# default private field expected:\n" + pageDefault);

            // App: [STAThread] appears only for IsWin32App; ComWrappers when UsingCSWinRT.
            string appWin32 = GenerateCSharp(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "IsWin32App", true);
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", true);
            });
            Assert.IsTrue(appWin32.Contains("[global::System.STAThreadAttribute]"),
                "C# IsWin32App should emit [STAThread]:\n" + appWin32);
            Assert.IsTrue(appWin32.Contains("global::WinRT.ComWrappersSupport.InitializeComWrappers();"),
                "C# UsingCSWinRT should emit InitializeComWrappers:\n" + appWin32);
        }

        private string GenerateCSharp(string xaml, bool isApplication, Action<CodeGeneratorProjectContext> configure = null)
        {
            CodeGeneratorProjectContext context = CreateContext(isApplication);
            if (configure != null)
            {
                configure(context);
            }
            return Generate(context, xaml, CodeGenLanguage.CSharp);
        }

        // ===== U1-9: DISABLE branch strings =====

        [TestMethod]
        public void U1_9_DisableBranch_EmitsTwoLayerHooks()
        {
            string code = GenerateVB(AppXaml, isApplication: true);

            Assert.IsTrue(code.Contains("Friend Module XamlGeneratedProgram"),
                "expected 'Friend Module XamlGeneratedProgram':\n" + code);
            Assert.IsTrue(code.Contains("Friend Shared Sub XamlGeneratedCreateApplicationInstance"),
                "expected outer 'Friend Shared Sub XamlGeneratedCreateApplicationInstance':\n" + code);
            Assert.IsTrue(code.Contains("Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance"),
                "expected inner 'Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance':\n" + code);
        }

        // ===== U1-10: WithEvents value-type downgrade =====
        //
        // Downgrade behavior (ii): a named element whose type is a value type
        // cannot carry WithEvents (BC30413), so the VB page pass1 should downgrade
        // to a plain field plus a self-documenting comment.  The normal
        // TestHelper.GenerateCodeBehind path runs the XamlDomValidator, which
        // rejects naming value types (WMC0045) *before* codegen - so this test
        // reproduces the codegen flow without validation, exactly the "校验被绕过 /
        // 旧 XAML / 本地未知类型漏点" edge the downgrade backstop serves.
        //
        // NOTE: the downgrade template branch (VisualBasicPagePass1.tt) implements
        // the downgrade behavior (ii): value-type fields emit a plain field (no
        // WithEvents) plus the comment below. This test asserts that branch is
        // active (no "WithEvents d" is emitted).

        [TestMethod]
        public void U1_10_ValueTypeNamedField_DowngradesToPlainField()
        {
            string xaml = @"
<Page
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyClass'>
    <Grid>
        <x:Double x:Name='d' />
    </Grid>
</Page>";

            string code = GenerateVbWithoutValidation(xaml, isApplication: false);

            Assert.IsTrue(code.Contains("' 值类型不支持 WithEvents，已输出普通字段"),
                "expected value-type downgrade comment:\n" + code);
            Assert.IsFalse(code.Contains("WithEvents d"),
                "value type must not emit WithEvents:\n" + code);
        }

        // Reproduces TestHelper.GenerateCodeBehind but skips XamlDomValidator
        // (which would reject naming a value type with WMC0045 before codegen).
        private string GenerateVbWithoutValidation(string xaml, bool isApplication)
        {
            CodeGeneratorProjectContext context = CreateContext(isApplication);
            DirectUISchemaContext schema = _testHelper.LoadSchema(SchemaMode.ManagedRuntime);

            CompilerDomRootToken domRoot = _testHelper.LoadXamlDom(xaml, schema);
            XamlClassCodeInfo classInfo = _testHelper.HarvestClassCodeInfo(context.ProjectPath, domRoot, true, isApplication);
            XamlFileCodeInfo fileInfo = _testHelper.HarvestFileCodeInfo(context.ProjectPath, true, classInfo, domRoot);

            string dummyFileName = "DummyFile.xaml";
            string dummyFilePath = System.IO.Path.Combine(context.ProjectPath, dummyFileName);
            File.WriteAllText(dummyFilePath, "");   // checksum computation reads this file
            fileInfo.ApparentRelativePath = dummyFileName;
            fileInfo.FullPathToXamlFile = dummyFilePath;
            fileInfo.RelativePathFromGeneratedCodeToXamlFile = dummyFileName;
            fileInfo.SourceXamlGivenPath = dummyFilePath;
            classInfo.AddXamlFileInfo(fileInfo);

            var codeLang = Language.Parse("VB");
            XamlCodeGenerator codeGenerator = new XamlCodeGenerator(codeLang, true, context.ProjectInfo, null);
            return Combine(codeGenerator.GenerateCodeBehind(classInfo));
        }
    }

    // ===========================================================================
    // L2: vbc / csc compile gates.
    //
    // Each test generates code with TestHelper (same path as the L1 tests), writes
    // it to a temp directory, then invokes the Roslyn compiler shipped with the
    // .NET SDK.  Reference assemblies come exclusively from the local NuGet cache
    // and the installed .NET ref packs - never from the network.  If the local
    // references cannot be found, the test is inconclusive (skipped) rather than
    // failing.
    // ===========================================================================

    [TestClass]
    public class VbFeatureParityCompileTests
    {
        TestHelper _testHelper;

        [TestInitialize]
        public void SchemaInit()
        {
            _testHelper = new TestHelper();
        }

        private const string PageXamlTemplate = @"
<Page
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyClass'>
    <Grid>
        <Button x:Name='btn'{0} />
    </Grid>
</Page>";

        private const string AppXaml = @"
<Application
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyApp'>
</Application>";

        private static string PageXaml(string fieldModifier)
        {
            string attr = String.IsNullOrEmpty(fieldModifier) ? "" : " x:FieldModifier='" + fieldModifier + "'";
            return String.Format(PageXamlTemplate, attr);
        }

        private string Generate(CodeGeneratorProjectContext context, string xaml, CodeGenLanguage lang)
        {
            DirectUISchemaContext schema = _testHelper.LoadSchema(SchemaMode.ManagedRuntime);
            List<string> xamlStrings = new List<string> { xaml };
            context.IsPass1 = true;
            List<FileNameAndContentPair> pass1 = _testHelper.GenerateCodeBehind(context, xamlStrings, schema, lang);
            context.IsPass1 = false;
            List<FileNameAndContentPair> pass2 = _testHelper.GenerateCodeBehind(context, xamlStrings, schema, lang);

            var sb = new StringBuilder();
            foreach (var pair in (pass1 ?? new List<FileNameAndContentPair>()).Concat(pass2 ?? new List<FileNameAndContentPair>()))
            {
                if (pair != null && pair.Contents != null)
                {
                    sb.AppendLine(pair.Contents);
                }
            }
            return sb.ToString();
        }

        private string GenerateVB(string xaml, bool isApplication, Action<CodeGeneratorProjectContext> configure = null)
        {
            var context = new CodeGeneratorProjectContext(new Version(KnownVersions.Latest));
            context.IsApplication = isApplication;
            if (configure != null)
            {
                configure(context);
            }
            return Generate(context, xaml, CodeGenLanguage.VisualBasic);
        }

        private string GenerateCSharp(string xaml, bool isApplication, Action<CodeGeneratorProjectContext> configure = null)
        {
            var context = new CodeGeneratorProjectContext(new Version(KnownVersions.Latest));
            context.IsApplication = isApplication;
            if (configure != null)
            {
                configure(context);
            }
            return Generate(context, xaml, CodeGenLanguage.CSharp);
        }

        // Reach through the XamlProjectInfo proxy to set an internal property.
        private static void SetProjectInfo(Win8Xaml.CompilerProxies.XamlProjectInfo proxy, string propertyName, object value)
        {
            FieldInfo f = typeof(Win8Xaml.CompilerProxies.XamlProjectInfo).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "XamlProjectInfo proxy '_instance' field not found");
            object instance = f.GetValue(proxy);
            PropertyInfo prop = instance.GetType().GetProperty(propertyName);
            Assert.IsNotNull(prop, "internal XamlProjectInfo property '{0}' not found", propertyName);
            prop.SetValue(instance, value, null);
        }

        // --- reference resolution (local only, never the network) ---

        private static string NuGetCacheRoot
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("USERPROFILE");
                return System.IO.Path.Combine(root, ".nuget", "packages");
            }
        }

        private static string[] ResolveLocalReferences(bool forCsWinRT)
        {
            var refs = new List<string>();

            string netCoreRef = null;
            string[] candidatePacks =
            {
                @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.11\ref\net10.0",
                @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\8.0.30\ref\net8.0",
            };
            foreach (string dir in candidatePacks)
            {
                if (Directory.Exists(dir))
                {
                    netCoreRef = dir;
                    break;
                }
            }
            Assert.IsNotNull(netCoreRef, "No .NET ref pack found; cannot run vbc/csc gate.");
            refs.AddRange(Directory.EnumerateFiles(netCoreRef, "*.dll"));

            // WinUI 3 projection + metadata.
            string nuget = NuGetCacheRoot;
            string winuiRoot = Directory.Exists(System.IO.Path.Combine(nuget, "microsoft.windowsappsdk.winui"))
                ? System.IO.Path.Combine(nuget, "microsoft.windowsappsdk.winui")
                : null;

            if (winuiRoot != null)
            {
                string[] versions = Directory.GetDirectories(winuiRoot).OrderByDescending(v => v).ToArray();
                if (versions.Length > 0)
                {
                    string pkg = versions[0];
                    string lib = Directory.GetDirectories(System.IO.Path.Combine(pkg, "lib")).OrderByDescending(v => v).FirstOrDefault();
                    if (lib != null)
                    {
                        refs.AddRange(Directory.EnumerateFiles(lib, "*.dll"));
                    }
                    string metadata = System.IO.Path.Combine(pkg, "metadata");
                    if (Directory.Exists(metadata))
                    {
                        refs.AddRange(Directory.EnumerateFiles(metadata, "*.winmd"));
                        refs.AddRange(Directory.EnumerateFiles(metadata, "*.dll"));
                    }
                }
            }

            // Windows SDK projection (WinRT.Runtime, Microsoft.Windows.SDK.NET).
            string sdkRefRoot = System.IO.Path.Combine(nuget, "microsoft.windows.sdk.net.ref");
            if (Directory.Exists(sdkRefRoot))
            {
                string sdkRef = Directory.GetDirectories(sdkRefRoot).OrderByDescending(v => v).FirstOrDefault();
                if (sdkRef != null)
                {
                    foreach (string dir in Directory.GetDirectories(sdkRef, "lib", SearchOption.AllDirectories))
                    {
                        refs.AddRange(Directory.EnumerateFiles(dir, "*.dll"));
                    }
                }
            }

            return refs.Distinct().ToArray();
        }

        private static bool TryFindCompiler(string compilerDllName, out string path)
        {
            string sdkRoot = @"C:\Program Files\dotnet\sdk";
            if (Directory.Exists(sdkRoot))
            {
                foreach (string sdk in Directory.GetDirectories(sdkRoot).OrderByDescending(v => v))
                {
                    string candidate = System.IO.Path.Combine(sdk, "Roslyn", "bincore", compilerDllName);
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                }
            }
            path = null;
            return false;
        }

        private static void CompileAndAssertZeroErrors(string generatedCode, string extension)
        {
            Assert.IsNotNull(generatedCode);
            Assert.IsTrue(generatedCode.Length > 0, "no generated code to compile");

            bool isVb = String.Equals(extension, ".vb", StringComparison.OrdinalIgnoreCase);
            string compilerDll = isVb ? "vbc.dll" : "csc.dll";
            string dotnetExe = @"C:\Program Files\dotnet\dotnet.exe";
            if (!File.Exists(dotnetExe))
            {
                Assert.Inconclusive("dotnet.exe not found; cannot run {0} gate.", compilerDll);
                return;
            }
            if (!TryFindCompiler(compilerDll, out string compilerPath))
            {
                Assert.Inconclusive("Roslyn {0} not found under the .NET SDK; cannot run compile gate.", compilerDll);
                return;
            }

            string[] references;
            try
            {
                references = ResolveLocalReferences(forCsWinRT: generatedCode.Contains("WinRT.ComWrappersSupport"));
            }
            catch (AssertFailedException)
            {
                Assert.Inconclusive("Local reference assemblies are not available; skipping {0} gate.", compilerDll);
                return;
            }

            // Write the generated source + a minimal entry stub to a temp folder.
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VbFeatureParity_Compile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string sourceFile = System.IO.Path.Combine(tempDir, "Generated" + extension);

                // Drive the compiler through a response file: robust against spaces
                // in SDK/NuGet paths and avoids ProcessStartInfo.ArgumentList, which
                // is not present in older .NET Framework reference assemblies.
                string rspFile = System.IO.Path.Combine(tempDir, "compile.rsp");
                var rspLines = new List<string>();
                rspLines.Add("/nologo");
                rspLines.Add("/noconfig");
                // vbc and csc spell this flag differently.
                rspLines.Add(isVb ? "/nostdlib" : "/nostdlib+");
                foreach (string r in references)
                {
                    rspLines.Add("/reference:\"" + r + "\"");
                }
                rspLines.Add("/out:\"" + System.IO.Path.Combine(tempDir, "out.dll") + "\"");
                rspLines.Add("\"" + sourceFile + "\"");
                File.WriteAllText(rspFile, String.Join(Environment.NewLine, rspLines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var psi = new ProcessStartInfo(dotnetExe, "\"" + compilerPath + "\" \"@" + rspFile + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(psi))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    string diag = stdout + "\n" + stderr;
                    Assert.AreEqual(0, process.ExitCode,
                        "{0} failed. {1}\nGenerated code:\n{2}", compilerDll, diag, generatedCode);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { /* temp cleanup best-effort */ }
            }
        }

        // ===== V1: Page with Friend/Public/Private WithEvents fields =====

        [TestMethod]
        public void V1_Page_FieldModifiers_CompileZeroErrors()
        {
            // One Page with all three modifiers; pass1+pass2 output combined.
            string xaml = @"
<Page
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
    x:Class='MyNamespace.MyClass'>
    <Grid>
        <Button x:Name='friendBtn' x:FieldModifier='internal' />
        <Button x:Name='publicBtn' x:FieldModifier='public' />
        <Button x:Name='privateBtn' />
    </Grid>
</Page>";

            string code = GenerateVB(xaml, isApplication: false);
            Assert.IsTrue(code.Contains("Friend WithEvents friendBtn"), "expected Friend field:\n" + code);
            Assert.IsTrue(code.Contains("Public WithEvents publicBtn"), "expected Public field:\n" + code);
            Assert.IsTrue(code.Contains("Private WithEvents privateBtn"), "expected Private field:\n" + code);

            CompileAndAssertZeroErrors(code, ".vb");
        }

        // ===== V2: Module Program (inline init, no thread attribute) =====

        [TestMethod]
        public void V2_App_ModuleProgram_InlineInit_CompileZeroErrors()
        {
            string code = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", true);
                SetProjectInfo(ctx.ProjectInfo, "EnabledXamlOptionalChanges", new List<string> { "5" });
            });

            Assert.IsTrue(code.Contains("Public Module Program"), "expected Module Program:\n" + code);
            Assert.IsTrue(code.Contains("Sub Main(ByVal args() As String)"), "expected Sub Main without Shared:\n" + code);
            Assert.IsFalse(code.Contains("Shared Sub Main"), "Sub Main must not be written Shared:\n" + code);

            CompileAndAssertZeroErrors(code, ".vb");
        }

        // ===== V3: DISABLE branch (two-layer hooks) =====

        [TestMethod]
        public void V3_DisableBranch_TwoLayerHooks_CompileZeroErrors()
        {
            string code = GenerateVB(AppXaml, isApplication: true);

            Assert.IsTrue(code.Contains("Friend Module XamlGeneratedProgram"), "expected DISABLE program module:\n" + code);
            Assert.IsTrue(code.Contains("Friend Shared Sub XamlGeneratedCreateApplicationInstance"), "expected outer hook:\n" + code);
            Assert.IsTrue(code.Contains("Private Shared Partial Sub _XamlGeneratedCreateApplicationInstance"), "expected inner partial declaration:\n" + code);

            CompileAndAssertZeroErrors(code, ".vb");
        }

        // ===== V4: CType(5, Global...XamlChangeId) App =====

        [TestMethod]
        public void V4_App_NumericXamlChangeId_CType_CompileZeroErrors()
        {
            string code = GenerateVB(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "EnabledXamlOptionalChanges", new List<string> { "5" });
            });

            Assert.IsTrue(code.Contains("CType(5, Global.Microsoft.UI.Xaml.Settings.XamlChangeId)"),
                "expected Globalized CType change id:\n" + code);

            CompileAndAssertZeroErrors(code, ".vb");
        }

        // ===== V5: C# same inputs =====

        [TestMethod]
        public void V5_CSharp_SameInputs_CompileZeroErrors()
        {
            string pageCode = GenerateCSharp(PageXaml("internal"), isApplication: false, configure: null);
            Assert.IsTrue(pageCode.Contains("internal global::"), "C# internal field expected:\n" + pageCode);
            CompileAndAssertZeroErrors(pageCode, ".cs");

            string appCode = GenerateCSharp(AppXaml, isApplication: true, configure: ctx =>
            {
                SetProjectInfo(ctx.ProjectInfo, "IsWin32App", true);
                SetProjectInfo(ctx.ProjectInfo, "UsingCSWinRT", true);
            });
            Assert.IsTrue(appCode.Contains("[global::System.STAThreadAttribute]"), "C# IsWin32App [STAThread] expected:\n" + appCode);
            CompileAndAssertZeroErrors(appCode, ".cs");
        }
    }
}
