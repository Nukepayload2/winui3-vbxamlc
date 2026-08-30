Imports Microsoft.UI.Xaml

' NOTE: the WinUI XAML compiler emits partials as "Namespace Global.DisableXamlGeneratedMainVb"
' (root-level, bypassing RootNamespace=DisableXamlGeneratedMainVb). VB prepends RootNamespace to
' a plain "Namespace DisableXamlGeneratedMainVb", which would produce a DIFFERENT type
' (DisableXamlGeneratedMainVb.DisableXamlGeneratedMainVb.App) and fail to merge -> BC30451.
' So code-behind must mirror Global.DisableXamlGeneratedMainVb.
Namespace Global.DisableXamlGeneratedMainVb

    Partial Public Class App
        Inherits Application

        Public Sub New()
            ' Explicit parameterless ctor: must call InitializeComponent() (omitting it -> BC40054).
            InitializeComponent()
        End Sub

        Protected Overrides Sub OnLaunched(ByVal args As LaunchActivatedEventArgs)
            Dim window As New MainWindow()
            window.Activate()
        End Sub

    End Class

End Namespace
