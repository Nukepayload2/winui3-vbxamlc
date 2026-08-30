Imports Microsoft.UI.Xaml

' NOTE: the WinUI XAML compiler emits partials as "Namespace Global.DisableXamlGeneratedMainNoCtorVb"
' (root-level, bypassing RootNamespace=DisableXamlGeneratedMainNoCtorVb). VB prepends RootNamespace
' to a plain "Namespace DisableXamlGeneratedMainNoCtorVb", which would produce a DIFFERENT type
' and fail to merge -> BC30451. So code-behind must mirror Global.<RootNamespace>.
Namespace Global.DisableXamlGeneratedMainNoCtorVb

    Partial Public Class App
        Inherits Application

        ' This sample intentionally has NO parameterless constructor. The developer
        ' supplies their own entry point (see Program.vb) and constructs the App with
        ' this parameterized constructor. Because DISABLE_XAML_GENERATED_MAIN is defined,
        ' the XamlCompiler must NOT emit the parameterless constructor call in the
        ' generated XamlGeneratedMain() helper (the inner partial
        ' _XamlGeneratedCreateApplicationInstance implementation is omitted, so the
        ' partial-method call is removed at compile time -> silent).
        Public Sub New(ByVal launchId As Integer)
            LaunchMarker = "CustomMain:" & launchId

            InitializeComponent()
        End Sub

        ' Recorded by the parameterized constructor above and displayed by MainWindow so
        ' that automated tests can verify that the app was launched by the developer's
        ' own entry point using this constructor.
        Public Shared Property LaunchMarker As String = String.Empty

        Protected Overrides Sub OnLaunched(ByVal args As LaunchActivatedEventArgs)
            Dim window As New MainWindow()
            window.Activate()
        End Sub

    End Class

End Namespace
