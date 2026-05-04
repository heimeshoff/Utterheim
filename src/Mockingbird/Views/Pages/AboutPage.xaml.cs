using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Tts;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// About page (main-017) — brand mark, tagline, version, engine status panel,
/// "View logs" shortcut, credits. Implements <see cref="INavigableView{T}"/>
/// (typed VM accessor) and <see cref="INavigationAware"/> (lifecycle hooks)
/// per ADR 0009.
///
/// On <c>OnNavigatedTo</c>: ask the VM to seed engine state from
/// <see cref="SidecarHost.GetStatus"/> and subscribe to
/// <see cref="SidecarHost.StateChanged"/>. On <c>OnNavigatedFrom</c>:
/// unsubscribe so the VM (transient) doesn't leak a handler when the next
/// instance attaches.
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
        ViewModel.Attach();
    }

    public void OnNavigatedFrom()
    {
        ViewModel.Detach();
    }
}
