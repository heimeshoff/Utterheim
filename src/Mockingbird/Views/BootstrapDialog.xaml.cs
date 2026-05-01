using System.Windows;
using Wpf.Ui.Controls;

namespace Mockingbird.Views;

/// <summary>
/// First-run dialog. Currently a placeholder per the main-009 scope amendment —
/// it acknowledges "Mockingbird is ready" without doing a real download.
/// Real model + runtime download UX lives in main-011.
/// </summary>
public partial class BootstrapDialog : FluentWindow
{
    public BootstrapDialog()
    {
        InitializeComponent();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
