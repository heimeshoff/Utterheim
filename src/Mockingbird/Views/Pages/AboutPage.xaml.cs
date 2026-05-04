using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Extensions.Logging;
using Mockingbird.Services;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// About page (main-032 redesign) — hero + profile/contact card + support /
/// GitHub card + credits, mirroring WhisperHeim's About. Pure identity
/// surface; engine diagnostics moved to Settings.
///
/// Implements <see cref="INavigableView{T}"/> (typed VM accessor) and
/// <see cref="INavigationAware"/> (lifecycle hooks) per ADR 0009. The
/// lifecycle hooks no longer touch the VM — there is nothing to subscribe
/// to — but the contract stays in place so the navigation shell logs
/// transitions consistently.
/// </summary>
public partial class AboutPage : Page, INavigableView<AboutPageViewModel>, INavigationAware
{
    private readonly ILogger<AboutPage> _logger;

    public AboutPageViewModel ViewModel { get; }

    public AboutPage(AboutPageViewModel viewModel, ILogger<AboutPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(AboutPage));
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// Open an external <c>http(s)</c> hyperlink in the user's default browser.
    /// Used by the heimeshoff.de / Bluesky / LinkedIn / GitHub links and the
    /// Ko-fi support button. Lifted verbatim from WhisperHeim's About page so
    /// behaviour is identical across the two apps.
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "About: failed to open hyperlink {Url}.", e.Uri.AbsoluteUri);
        }
        finally
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ko-fi support button click — opens <see cref="AppInfo.KofiUrl"/> in the
    /// default browser. Bound directly in code-behind (no command) because the
    /// URL is an app-wide constant, not a per-instance VM value.
    /// </summary>
    private void KofiButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.KofiUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "About: failed to open Ko-fi URL {Url}.", AppInfo.KofiUrl);
        }
    }
}
