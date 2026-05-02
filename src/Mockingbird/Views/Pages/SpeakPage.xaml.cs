using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Mockingbird.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Mockingbird.Views.Pages;

/// <summary>
/// Default landing page (per styleguide). Stub in main-020 — main-013 ships
/// the real Speak surface (text input, voice selection, Play/Stop/Save).
///
/// Implements both <see cref="INavigableView{T}"/> (for the typed VM the
/// shell binds) and <see cref="INavigationAware"/> (for the OnNavigatedTo
/// lifecycle hook the feature tasks rely on — see ADR 0009 + main-020 Q1).
/// </summary>
public partial class SpeakPage : Page, INavigableView<SpeakPageViewModel>, INavigationAware
{
    private readonly ILogger<SpeakPage> _logger;

    public SpeakPageViewModel ViewModel { get; }

    public SpeakPage(SpeakPageViewModel viewModel, ILogger<SpeakPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = this;
        InitializeComponent();
    }

    public void OnNavigatedTo()
    {
        _logger.LogInformation("Navigated to {PageName}", nameof(SpeakPage));
    }

    public void OnNavigatedFrom()
    {
    }
}
