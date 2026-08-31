Imports Microsoft.UI.Xaml

Partial Public Class MainWindow
    Inherits Window

    Public Sub New()
        InitializeComponent()

        ' Surface the marker recorded by our custom entry point so that
        ' automated tests can verify the app really launched through Program.Main.
        entryPointTextBlock.Text = Program.LaunchMarker
    End Sub

End Class
