' This sample defines DISABLE_XAML_GENERATED_MAIN AND intentionally omits a parameterless
' App constructor. The developer supplies their own entry point below and constructs the App
' explicitly using its parameterized constructor.
'
' The XamlCompiler still generates a XamlGeneratedProgram.XamlGeneratedMain() helper, but
' because the App has no parameterless constructor it must NOT emit the "New App()" call there
' (otherwise this project would fail to compile). This project exists to verify that behavior
' (the inner partial entity is missing, so the XamlGeneratedCreateApplicationInstance
' call is ignored = silent).
'
' NOTE: must be Partial because Nukepayload2.UI.VBWinUI3 injects a module initializer
' (Sub New) into "Partial Module Program" that loads the self-contained WinUI runtime.
Partial Module Program

    ' No thread-model attribute here on purpose: the VB compiler synthesizes
    ' STAThreadAttribute on the compiled entry point, so the entry thread is STA.
    Sub Main(ByVal args() As String)
        Global.WinRT.ComWrappersSupport.InitializeComWrappers()
        Microsoft.UI.Xaml.Application.Start(
            Sub(p)
                Dim context As New Global.Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Global.Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread())
                Global.System.Threading.SynchronizationContext.SetSynchronizationContext(context)
                Dim app As New App(42)
            End Sub)
    End Sub

End Module
