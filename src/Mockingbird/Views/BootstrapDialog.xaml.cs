using System.Windows;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Tts;
using Wpf.Ui.Controls;

namespace Mockingbird.Views;

/// <summary>
/// First-run dialog that drives <see cref="PythonRuntimeBootstrapper"/>:
/// downloads embeddable Python, pip-installs pocket-tts, smoke-tests the import.
/// Mirrors the WhisperHeim ModelDownloadDialog pattern (per ADR 0008): per-step
/// status text, per-step progress, cancel/retry, and persisted state so a
/// half-finished bootstrap survives a restart.
/// </summary>
public partial class BootstrapDialog : FluentWindow
{
    private readonly PythonRuntimeBootstrapper _bootstrapper;
    private readonly ILogger<BootstrapDialog> _logger;
    private CancellationTokenSource? _cts;

    /// <summary>True iff the bootstrap succeeded end-to-end.</summary>
    public bool BootstrapSucceeded { get; private set; }

    public BootstrapDialog(PythonRuntimeBootstrapper bootstrapper, ILogger<BootstrapDialog> logger)
    {
        _bootstrapper = bootstrapper;
        _logger = logger;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RunBootstrapAsync();
    }

    private async Task RunBootstrapAsync()
    {
        _cts = new CancellationTokenSource();
        RetryButton.Visibility = Visibility.Collapsed;
        ActionButton.Content = "Cancel";
        ActionButton.IsEnabled = true;

        var progress = new Progress<BootstrapProgress>(OnProgress);

        try
        {
            await _bootstrapper.BootstrapAsync(progress, _cts.Token);
            BootstrapSucceeded = true;
            StatusText.Text = "Mockingbird is ready.";
            DetailText.Text = "You can now send speak requests.";
            OverallProgress.Value = 100;
            StepCounter.Text = "Done";
            ActionButton.Content = "Continue";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Setup cancelled.";
            DetailText.Text = "Mockingbird will exit. Re-launch to resume.";
            ActionButton.Content = "Close";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootstrap failed");
            StatusText.Text = "Setup failed.";
            DetailText.Text = ex.Message;
            ActionButton.Content = "Close";
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(BootstrapProgress p)
    {
        StatusText.Text = p.Message;
        DetailText.Text = StepDescription(p.Step);
        StepCounter.Text = $"Step {(int)p.Step + 1} of 4 — {p.Step}";

        // Combine step + within-step fraction into a 0..100 overall percent.
        const int totalSteps = 4;
        var overall = ((int)p.Step + p.Fraction) / totalSteps * 100.0;
        OverallProgress.Value = Math.Clamp(overall, 0, 100);
    }

    private static string StepDescription(BootstrapStep step) => step switch
    {
        BootstrapStep.DownloadPython => "Downloading the embedded Python 3.12 runtime (~30 MB).",
        BootstrapStep.InstallPip => "Bootstrapping pip into the runtime.",
        BootstrapStep.InstallPocketTts => "Installing pocket-tts and PyTorch CPU (~600 MB).",
        BootstrapStep.SmokeTest => "Verifying the install.",
        _ => "",
    };

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            // In-flight cancel.
            _cts.Cancel();
            ActionButton.IsEnabled = false;
            StatusText.Text = "Cancelling…";
        }
        else
        {
            // Done or failed — close.
            DialogResult = BootstrapSucceeded;
            Close();
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBootstrapAsync();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Cancel an in-flight run rather than tearing the window down mid-pip-install.
        if (_cts is not null)
        {
            e.Cancel = true;
            _cts.Cancel();
            StatusText.Text = "Cancelling…";
            ActionButton.IsEnabled = false;
        }
        base.OnClosing(e);
    }
}
