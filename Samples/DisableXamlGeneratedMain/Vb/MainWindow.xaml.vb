Imports Microsoft.UI.Xaml

' Mirrors the XAML compiler's "Namespace Global.DisableXamlGeneratedMainVb" (see App.xaml.vb comment
' re: RootNamespace prepending workaround).
Namespace Global.DisableXamlGeneratedMainVb

    Partial Public Class MainWindow
        Inherits Window

        Public Sub New()
            InitializeComponent()

            ' Surface the marker recorded by our custom entry point so that
            ' automated tests can verify the app really launched through Program.Main.
            entryPointTextBlock.Text = Program.LaunchMarker
        End Sub

    End Class

End Namespace
