Imports Microsoft.UI.Xaml

Partial Public Class App
    Inherits Application

    ' This sample intentionally has NO parameterless constructor. The developer
    ' supplies their own entry point (see Program.vb) and constructs the App with
    ' this parameterized constructor. Because DISABLE_XAML_GENERATED_MAIN is defined,
    ' the XamlCompiler must NOT emit the parameterless constructor call in the
    ' generated XamlGeneratedMain() helper (the inner partial
    ' _XamlGeneratedCreateApplicationInstance implementation is omitted, so the
    ' partial-method call is removed at compile time -> silent).
    Public Sub New(launchId As Integer)
        LaunchMarker = "CustomMain:" & launchId

        InitializeComponent()
    End Sub

    ' Recorded by the parameterized constructor above and displayed by MainWindow so
    ' that automated tests can verify that the app was launched by the developer's
    ' own entry point using this constructor.
    Public Shared Property LaunchMarker As String = String.Empty

    Protected Overrides Sub OnLaunched(args As LaunchActivatedEventArgs)
        Dim window As New MainWindow()
        window.Activate()
    End Sub

End Class
