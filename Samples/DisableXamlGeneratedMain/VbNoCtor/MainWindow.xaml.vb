Imports Microsoft.UI.Xaml

' Mirrors the XAML compiler's "Namespace Global.DisableXamlGeneratedMainNoCtorVb" (see
' App.xaml.vb comment re: RootNamespace prepending workaround).
Namespace Global.DisableXamlGeneratedMainNoCtorVb

    Partial Public Class MainWindow
        Inherits Window

        Public Sub New()
            InitializeComponent()

            ' Surface the marker recorded by App's parameterized constructor so that
            ' automated tests can verify the app really launched through Program.Main
            ' and the custom App(int) constructor.
            entryPointTextBlock.Text = App.LaunchMarker
        End Sub

    End Class

End Namespace
