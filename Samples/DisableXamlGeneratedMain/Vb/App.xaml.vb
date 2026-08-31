Imports Microsoft.UI.Xaml

Partial Public Class App
    Inherits Application

    Public Sub New()
        ' Explicit parameterless ctor: must call InitializeComponent() (omitting it -> BC40054).
        InitializeComponent()
    End Sub

    Protected Overrides Sub OnLaunched(args As LaunchActivatedEventArgs)
        Dim window As New MainWindow()
        window.Activate()
    End Sub

End Class
