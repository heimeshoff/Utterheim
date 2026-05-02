using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Stub About page in main-020 — main-017 ships the brand mark, version
/// info, engine-status panel, and credits.
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
}
