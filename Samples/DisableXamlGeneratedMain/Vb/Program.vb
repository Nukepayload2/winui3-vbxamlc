' Custom entry point: with DISABLE_XAML_GENERATED_MAIN defined, the user supplies their own
' Sub Main and delegates to the XamlCompiler-generated startup helper
' XamlGeneratedProgram.XamlGeneratedMain().
'
' NOTE: must be Partial because Nukepayload2.UI.VBWinUI3 injects a module initializer
' (Sub New) into "Partial Module Program" that loads the self-contained WinUI runtime.
Partial Module Program

    ' Recorded by our custom entry point below and displayed by MainWindow so that
    ' automated tests can verify that this Main - and not a XamlCompiler-generated
    ' one - is what started the app.
    Public Const LaunchMarker As String = "CustomMain"

    ' No thread-model attribute here on purpose: the VB compiler synthesizes
    ' STAThreadAttribute on the compiled entry point (Roslyn SourceMethodSymbol.vb
    ' AddSynthesizedAttributes), so the entry thread is STA.
    Sub Main(ByVal args() As String)
        ' Delegate to the generated startup helper (mirrors Cs Program.cs:16).
        Global.DisableXamlGeneratedMainVb.XamlGeneratedProgram.XamlGeneratedMain()
    End Sub

End Module
