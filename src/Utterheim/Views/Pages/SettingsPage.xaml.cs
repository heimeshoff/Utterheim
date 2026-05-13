using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Settings;
using Utterheim.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Utterheim.Views.Pages;

/// <summary>
/// Settings page (main-016) — Audio / App / Diagnostics setting cards plus
/// the Appearance picker (main-029) per the styleguide. Implements
/// <see cref="INavigableView{T}"/> (typed VM) and <see cref="INavigationAware"/>
/// (lifecycle hooks per ADR 0009).
///
/// On <c>OnNavigatedTo</c>: re-enumerate WaveOut devices and re-read the
/// HKCU\…\Run state (registry can be mutated externally between visits) by
/// delegating to <see cref="SettingsPageViewModel.LoadAsync"/>; also redraw
/// the Appearance picker's selected-tile highlight from
/// <see cref="UserSettings.AppearanceMode"/>.
///
/// The Appearance picker's click handlers persist the user's choice to
/// settings.json via <see cref="UserSettings.AppearanceMode"/> and apply the
/// theme live via <c>Wpf.Ui.Appearance.ApplicationThemeManager</c> per
/// <c>knowledge/research/wpfui-live-theme-swap-2026-05-04.md</c>.
/// </summary>
public partial class SettingsPage : Page, INavigableView<SettingsPageViewModel>, INavigationAware
{
    // Selected-tile highlight: subtle 10% blue (#19005FAA) per WhisperHeim.
    private static readonly SolidColorBrush SelectedTileBrush =
        new(Color.FromArgb(0x19, 0x00, 0x5F, 0xAA));

    private readonly ILogger<SettingsPage> _logger;
    private readonly UserSettings _userSettings;
    private CancellationTokenSource? _loadCts;

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage(
        SettingsPageViewModel viewModel,
        UserSettings userSettings,
        ILogger<SettingsPage> logger)
    {
        ViewModel = viewModel;
        _userSettings = userSettings;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public async void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(SettingsPage));

        HighlightActiveAppearanceTile();
        // Subscribe to DataPathChanged so the diagnostics card stays live if any
        // surface (Browse… / Reset, or a future external mutation) calls
        // SetDataPath while this page is visible (main-031).
        ViewModel.Attach();

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
        ViewModel.Detach();

        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    // ─── Appearance picker click handlers (main-029) ────────────────────────

    private void ThemeLight_Click(object sender, MouseButtonEventArgs e) =>
        ApplyAppearance(AppearanceMode.Light);

    private void ThemeDark_Click(object sender, MouseButtonEventArgs e) =>
        ApplyAppearance(AppearanceMode.Dark);

    private void ThemeSystem_Click(object sender, MouseButtonEventArgs e) =>
        ApplyAppearance(AppearanceMode.System);

    /// <summary>
    /// Persist the user's choice to settings.json and apply the theme live.
    /// Mirrors WhisperHeim's <c>ApplyTheme</c> (<see href="GeneralPage.xaml.cs"/>
    /// lines 79-106) end-to-end: write first, swap second, re-highlight third.
    /// </summary>
    private void ApplyAppearance(AppearanceMode mode)
    {
        try
        {
            // 1. Persist (the property setter short-circuits on no-op so a
            //    same-tile re-click does not rewrite settings.json).
            _userSettings.AppearanceMode = mode;

            // 2. Apply live via ApplicationThemeManager. Theme brushes are
            //    DynamicResource throughout the app, so wpfui controls + our
            //    own XAML re-render automatically. Brand brushes are fixed
            //    SolidColorBrush StaticResources and stay theme-independent.
            EntryPoint.ApplyAppearanceMode(mode);

            // 3. Redraw the active-tile highlight.
            HighlightActiveAppearanceTile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: failed to apply AppearanceMode {Mode}.", mode);
        }
    }

    /// <summary>
    /// Set the selected tile's background to <see cref="SelectedTileBrush"/>
    /// and the others to <see cref="Brushes.Transparent"/>. Mirrors WhisperHeim
    /// <c>HighlightActiveTheme()</c>.
    /// </summary>
    private void HighlightActiveAppearanceTile()
    {
        var current = _userSettings.AppearanceMode;
        ThemeLight.Background = current == AppearanceMode.Light ? SelectedTileBrush : Brushes.Transparent;
        ThemeDark.Background = current == AppearanceMode.Dark ? SelectedTileBrush : Brushes.Transparent;
        ThemeSystem.Background = current == AppearanceMode.System ? SelectedTileBrush : Brushes.Transparent;
    }
}
