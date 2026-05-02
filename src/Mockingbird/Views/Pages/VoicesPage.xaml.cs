using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Stub Voices page in main-020 — main-014 ships the voice library and
/// capture-flow entry point.
/// </summary>
public partial class VoicesPage : Page, INavigableView<VoicesPageViewModel>, INavigationAware
{
    private readonly ILogger<VoicesPage> _logger;

    public VoicesPageViewModel ViewModel { get; }

    public VoicesPage(VoicesPageViewModel viewModel, ILogger<VoicesPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(VoicesPage));
    }

    public void OnNavigatedFrom()
    {
    }
}
