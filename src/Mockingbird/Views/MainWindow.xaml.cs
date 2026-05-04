using System.ComponentModel;
using System.Windows;
using Mockingbird.Services.Speak;
using Mockingbird.ViewModels;
using Mockingbird.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Mockingbird.Views;

/// <summary>
/// Tray-resident shell window. Hosts a wpfui <c>NavigationView</c> with the
/// canonical four-page set (Speak / Voices / Settings / About) per ADR 0009,
/// plus a thin persistent status footer (per main-020 Q4) bound to
/// <see cref="EngineStatusViewModel"/>. Closing the window hides to tray;
/// the tray menu owns shutdown.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly SpeakQueue? _queue;
    private readonly Action? _exitAction;
    private readonly IPageService? _pageService;
    private readonly IContentDialogService? _contentDialogService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        SpeakQueue queue,
        Action exitAction,
        IPageService pageService,
        EngineStatusViewModel engineStatus,
        IContentDialogService contentDialogService) : this()
    {
        _queue = queue;
        _exitAction = exitAction;
        _pageService = pageService;
        _contentDialogService = contentDialogService;
        StatusFooter.DataContext = engineStatus;

        // wpfui's NavigationView uses IPageService to resolve page instances
        // through DI (per ADR 0009). The IsSelected="True" on the Speak entry
        // drives the initial navigation once the service is wired up; if
        // wpfui 3.1.x doesn't navigate from IsSelected alone, Loaded falls
        // back to an explicit Navigate call.
        RootNavigation.SetPageService(_pageService);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bind the ContentDialog host (main-026) — the per-row Delete dialog
        // on the Voices page renders into this presenter as a Mica-friendly
        // in-window modal rather than a secondary Window subclass.
        _contentDialogService?.SetDialogHost(RootContentDialogPresenter);

        if (RootNavigation.SelectedItem is null)
        {
            RootNavigation.Navigate(typeof(SpeakPage));
        }
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
