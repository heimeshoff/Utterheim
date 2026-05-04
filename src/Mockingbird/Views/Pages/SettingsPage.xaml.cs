using System.Threading;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Settings page (main-016) — three sections of Fluent setting cards (Audio,
/// App, Diagnostics) per the styleguide. Implements
/// <see cref="INavigableView{T}"/> (typed VM) and <see cref="INavigationAware"/>
/// (lifecycle hooks per ADR 0009).
///
/// On <c>OnNavigatedTo</c>: re-enumerate WaveOut devices and re-read the
/// HKCU\…\Run state (registry can be mutated externally between visits) by
/// delegating to <see cref="SettingsPageViewModel.LoadAsync"/>.
/// </summary>
public partial class SettingsPage : Page, INavigableView<SettingsPageViewModel>, INavigationAware
{
    private readonly ILogger<SettingsPage> _logger;
    private CancellationTokenSource? _loadCts;

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage(SettingsPageViewModel viewModel, ILogger<SettingsPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public async void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(SettingsPage));

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        try
        {
            await ViewModel.LoadAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings load failed.");
        }
    }

    public void OnNavigatedFrom()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }
}
