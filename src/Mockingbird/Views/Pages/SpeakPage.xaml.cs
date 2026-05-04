using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Speak;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Default landing page (per styleguide). main-013 fills it with the real Speak
/// surface (text input, voice picker, Play / Stop / Save).
///
/// Implements both <see cref="INavigableView{T}"/> (typed VM the shell binds)
/// and <see cref="INavigationAware"/> (lifecycle hooks per ADR 0009 + main-020 Q1).
/// </summary>
public partial class SpeakPage : Page, INavigableView<SpeakPageViewModel>, INavigationAware
{
    private readonly ILogger<SpeakPage> _logger;
    private readonly SpeakService _speakService;
    private readonly VoiceCatalog _voiceCatalog;
    private CancellationTokenSource? _refreshCts;

    public SpeakPageViewModel ViewModel { get; }

    public SpeakPage(
        SpeakPageViewModel viewModel,
        SpeakService speakService,
        VoiceCatalog voiceCatalog,
        ILogger<SpeakPage> logger)
    {
        ViewModel = viewModel;
        _speakService = speakService;
        _voiceCatalog = voiceCatalog;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public async void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(SpeakPage));

        _speakService.StatusChanged += OnStatusChanged;
        _voiceCatalog.VoicesChanged += OnVoicesChanged;
        ViewModel.ApplyStatus(_speakService.CurrentStatus);

        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try
        {
            await ViewModel.RefreshVoicesAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial voice refresh failed.");
        }
    }

    public void OnNavigatedFrom()
    {
        _speakService.StatusChanged -= OnStatusChanged;
        _voiceCatalog.VoicesChanged -= OnVoicesChanged;

        var cts = Interlocked.Exchange(ref _refreshCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    private void OnStatusChanged(object? sender, SpeakStatus status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ViewModel.ApplyStatus(status);
        else
            dispatcher.BeginInvoke(() => ViewModel.ApplyStatus(status));
    }

    private void OnVoicesChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = ViewModel.RefreshVoicesAsync(CancellationToken.None);
        }
        else
        {
            dispatcher.BeginInvoke(() => _ = ViewModel.RefreshVoicesAsync(CancellationToken.None));
        }
    }
}
