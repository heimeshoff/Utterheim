using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Stub Settings page in main-020 — main-016 ships port, output device,
/// hotkey window, and data-path management.
/// </summary>
public partial class SettingsPage : Page, INavigableView<SettingsPageViewModel>, INavigationAware
{
    private readonly ILogger<SettingsPage> _logger;

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage(SettingsPageViewModel viewModel, ILogger<SettingsPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(SettingsPage));
    }

    public void OnNavigatedFrom()
    {
    }
}
