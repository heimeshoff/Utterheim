using System.ComponentModel;
using System.Windows;
using Mockingbird.Services.Speak;
using Wpf.Ui.Controls;

namespace Mockingbird.Views;

/// <summary>
/// Skeleton main window. Closing it hides to tray rather than exiting the app
/// (per ADR 0001 / vision: tray-resident behaviour). Tray menu owns shutdown.
/// Real frontend lands in main-010 onward.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly SpeakQueue? _queue;
    private readonly Action? _exitAction;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(SpeakQueue queue, Action exitAction) : this()
    {
        _queue = queue;
        _exitAction = exitAction;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayStop_Click(object sender, RoutedEventArgs e)
    {
        _queue?.StopAndDrain();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _exitAction?.Invoke();
    }
}
