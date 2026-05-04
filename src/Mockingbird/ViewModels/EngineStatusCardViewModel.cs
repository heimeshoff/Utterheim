using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Settings;
using Mockingbird.Services.Tts;

namespace Mockingbird.ViewModels;

/// <summary>
/// View-model for the engine-status card and View-logs link surfaced on the
/// Settings page (main-032). Extracted from the former
/// <c>AboutPageViewModel</c> so the diagnostics surface lives where users
/// expect to manage app behaviour, while About becomes a pure identity /
/// credits page mirroring WhisperHeim's About.
///
/// <para>
/// Composed into <see cref="Pages.SettingsPageViewModel"/> as
/// <c>EngineStatus</c>, mirroring the
/// <c>VoicesPageViewModel.Cloning</c> sub-VM pattern (main-025). Registered
/// transient in DI so each Settings-page resolution gets a fresh instance.
/// </para>
///
/// <para>
/// <b>Engine status data flow</b>: subscribes to
/// <see cref="SidecarHost.StateChanged"/> on <see cref="Attach"/> (the same
/// event the footer <see cref="EngineStatusViewModel"/> consumes); re-seeds
/// via <see cref="SidecarHost.GetStatus"/> at the same moment. <see cref="Detach"/>
/// removes the handler so navigating away cannot leak. The page does
/// <em>not</em> call <c>GET /status</c> — that endpoint is the contract for
/// outside callers, while in-process subscribers stay event-driven and
/// dispatcher-thread-safe (per ADR 0018).
/// </para>
///
/// <para>
/// <b>Restart Engine</b> calls <see cref="SidecarHost.RestartAsync"/>; the
/// button is disabled while the engine is in a transitional state so the user
/// can't double-fire mid-cycle.
/// </para>
/// </summary>
public sealed partial class EngineStatusCardViewModel : ObservableObject
{
    private readonly SidecarHost? _sidecar;
    private readonly DataPathService _paths;
    private readonly ILogger<EngineStatusCardViewModel> _logger;

    public EngineStatusCardViewModel(
        DataPathService paths,
        ILogger<EngineStatusCardViewModel> logger,
        SidecarHost? sidecar = null)
    {
        _paths = paths;
        _logger = logger;
        _sidecar = sidecar;
    }

    // ─── Engine status panel ────────────────────────────────────────────────

    /// <summary>Coarse engine state — drives the pip brush and the friendly label.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineStateLabel))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsRetryEnabled))]
    [NotifyCanExecuteChangedFor(nameof(RestartEngineCommand))]
    private SidecarState _engineState = SidecarState.NotStarted;

    /// <summary>True iff the most recent <c>/health</c> probe succeeded — drives the green check / red dismiss icon.</summary>
    [ObservableProperty]
    private bool _healthy;

    /// <summary>Bound HTTP port of the sidecar, or 0 when not yet listening.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortLabel))]
    private int _port;

    /// <summary>Most recent error surfaced by the sidecar, or null. Wraps in the inline error block.</summary>
    [ObservableProperty]
    private string? _lastError;

    /// <summary>Friendly state label — same mapping as the persistent footer (<see cref="SidecarStateLabels"/>).</summary>
    public string EngineStateLabel => SidecarStateLabels.Format(EngineState);

    /// <summary>True iff state is <c>Running</c> — drives Healthy icon visibility (in non-running states the state label already covers the signal).</summary>
    public bool IsRunning => EngineState == SidecarState.Running;

    /// <summary><c>127.0.0.1:{port}</c> when listening, em-dash otherwise.</summary>
    public string PortLabel => Port > 0 ? $"127.0.0.1:{Port}" : "—";

    /// <summary>
    /// True when the Restart button should be enabled. Allowed on terminal
    /// states (<c>Running</c>, <c>Failed</c>, <c>NotStarted</c>) so the user
    /// can also restart a stuck running engine; disabled mid-transition so
    /// the user can't double-fire while the supervisor is already churning.
    /// </summary>
    public bool IsRetryEnabled => EngineState is SidecarState.Running
        or SidecarState.Failed
        or SidecarState.NotStarted;

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// Re-seed engine state from the host's current snapshot and subscribe to
    /// <see cref="SidecarHost.StateChanged"/>. Called by the Settings page's
    /// <c>OnNavigatedTo</c>; idempotent if already subscribed (defensive
    /// detach-then-attach).
    /// </summary>
    public void Attach()
    {
        if (_sidecar is null)
        {
            // Stub-engine path — no live updates. Show a sensible static label.
            EngineState = SidecarState.NotStarted;
            Healthy = false;
            Port = 0;
            LastError = null;
            return;
        }

        ApplyStatus(_sidecar.GetStatus());

        // Defensive: remove any prior subscription before re-adding to avoid
        // double-fires if Attach is called twice without an intervening Detach.
        _sidecar.StateChanged -= OnSidecarStateChanged;
        _sidecar.StateChanged += OnSidecarStateChanged;
    }

    /// <summary>Unsubscribe from sidecar updates. Called by <c>OnNavigatedFrom</c>.</summary>
    public void Detach()
    {
        if (_sidecar is null) return;
        _sidecar.StateChanged -= OnSidecarStateChanged;
    }

    private void OnSidecarStateChanged(object? sender, SidecarStatus status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyStatus(status);
        else
            dispatcher.BeginInvoke(() => ApplyStatus(status));
    }

    private void ApplyStatus(SidecarStatus status)
    {
        EngineState = status.State;
        Healthy = status.Healthy;
        Port = status.Port;
        LastError = status.LastError;
    }

    // ─── Commands ───────────────────────────────────────────────────────────

    /// <summary>
    /// Restart the engine: <c>StopAsync</c>-equivalent teardown plus a fresh
    /// supervisor task per <see cref="SidecarHost.RestartAsync"/>. Disabled
    /// while the engine is in a transitional state.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestartEngine))]
    private async Task RestartEngineAsync(CancellationToken ct)
    {
        if (_sidecar is null)
        {
            _logger.LogInformation("Restart Engine pressed but no SidecarHost is registered (stub-engine mode).");
            return;
        }

        try
        {
            await _sidecar.RestartAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: SidecarHost.RestartAsync failed.");
        }
    }

    private bool CanRestartEngine() => IsRetryEnabled;

    /// <summary>
    /// Open <c>%LOCALAPPDATA%\Mockingbird\logs\</c> in Explorer. If the directory
    /// doesn't exist (first launch before the first roll) open its parent
    /// instead, never throw.
    /// </summary>
    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            var logsPath = _paths.LogsPath;
            string target;
            if (Directory.Exists(logsPath))
            {
                target = logsPath;
            }
            else
            {
                // Fall back to the parent (LocalRoot) so the user always lands
                // somewhere familiar.
                var parent = Directory.GetParent(logsPath)?.FullName;
                target = parent is not null && Directory.Exists(parent) ? parent : DataPathService.LocalRoot;
                Directory.CreateDirectory(target);
                _logger.LogInformation("Settings: logs folder not yet present at {Path}; opening parent {Parent}.", logsPath, target);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{target}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: OpenLogs failed.");
        }
    }
}
