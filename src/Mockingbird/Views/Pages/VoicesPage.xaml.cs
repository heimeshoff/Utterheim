using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Speak;
using Mockingbird.Services.Tts;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Voices page (main-014) — voice library list with per-row Preview routed
/// through <see cref="SpeakService.Enqueue"/> per ADR 0014. Implements
/// <see cref="INavigableView{T}"/> (typed VM) and <see cref="INavigationAware"/>
/// (lifecycle hooks per ADR 0009 + main-020 Q1).
///
/// On <c>OnNavigatedTo</c>: refresh the catalog, subscribe to
/// <see cref="VoiceCatalog.VoicesChanged"/>, <see cref="SpeakService.StatusChanged"/>,
/// and <see cref="SidecarHost.StateChanged"/>. On <c>OnNavigatedFrom</c>:
/// unsubscribe and cancel any in-flight refresh.
///
/// Per the BC README's "no library.json reads here" gate, this page
/// consumes <see cref="VoiceCatalog"/> exclusively. main-015 will add the
/// cloned-voice surface to the catalog without changing this page.
/// </summary>
public partial class VoicesPage : Page, INavigableView<VoicesPageViewModel>, INavigationAware
{
    private readonly ILogger<VoicesPage> _logger;
    private readonly SpeakService _speakService;
    private readonly VoiceCatalog _voiceCatalog;
    private readonly SidecarHost? _sidecarHost;
    private CancellationTokenSource? _refreshCts;

    public VoicesPageViewModel ViewModel { get; }

    public VoicesPage(
        VoicesPageViewModel viewModel,
        SpeakService speakService,
        VoiceCatalog voiceCatalog,
        ILogger<VoicesPage> logger,
        SidecarHost? sidecarHost = null)
    {
        ViewModel = viewModel;
        _speakService = speakService;
        _voiceCatalog = voiceCatalog;
        _logger = logger;
        _sidecarHost = sidecarHost;
        DataContext = this;
        InitializeComponent();
    }

    public async void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(VoicesPage));

        _speakService.StatusChanged += OnStatusChanged;
        _voiceCatalog.VoicesChanged += OnVoicesChanged;
        if (_sidecarHost is not null)
            _sidecarHost.StateChanged += OnSidecarStateChanged;

        // Seed engine state and current speak status before the first refresh,
        // so the loading / error placeholders appear immediately on slow boot.
        var initialState = _sidecarHost?.GetStatus().State ?? SidecarState.Running;
        ViewModel.ApplyEngineState(initialState);
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
        if (_sidecarHost is not null)
            _sidecarHost.StateChanged -= OnSidecarStateChanged;

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
            _ = ViewModel.RefreshVoicesAsync(CancellationToken.None);
        else
            dispatcher.BeginInvoke(() => _ = ViewModel.RefreshVoicesAsync(CancellationToken.None));
    }

    private void OnSidecarStateChanged(object? sender, SidecarStatus status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            HandleSidecarState(status.State);
        else
            dispatcher.BeginInvoke(() => HandleSidecarState(status.State));
    }

    private void HandleSidecarState(SidecarState state)
    {
        var wasRunning = ViewModel.EngineState == SidecarState.Running;
        ViewModel.ApplyEngineState(state);

        // When the engine flips into 'running' from a starting/restarting
        // state, force a refresh so the list populates without re-navigation.
        if (state == SidecarState.Running && !wasRunning)
            _ = ViewModel.RefreshVoicesAsync(CancellationToken.None);
    }
}
