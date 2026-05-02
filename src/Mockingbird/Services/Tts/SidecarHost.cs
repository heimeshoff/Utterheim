using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Settings;

namespace Mockingbird.Services.Tts;

/// <summary>
/// Owns the lifecycle of the pocket-tts Python sidecar process per ADR 0002:
///
///  - Spawns <c>python.exe -m pocket_tts serve --host 127.0.0.1 --port N</c>.
///  - Polls <c>GET /health</c> until ready (the engine awaits <see cref="EnsureReadyAsync"/> before synthesis).
///  - Captures stdout/stderr line-by-line into Serilog with a "sidecar" source enrichment.
///  - Tears down with graceful kill-on-timeout when the host stops.
///  - Restarts on crash with capped exponential backoff; gives up after a few attempts.
///
/// Bound to loopback only (vision: no network exposure of TTS).
/// </summary>
public sealed class SidecarHost : IHostedService, IDisposable
{
    private static readonly Regex PortRegex = new(@"Uvicorn running on https?://[^:]+:(\d+)",
        RegexOptions.Compiled);

    private readonly DataPathService _paths;
    private readonly PythonRuntimeBootstrapper _bootstrapper;
    private readonly ILogger<SidecarHost> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly object _stateLock = new();
    private SidecarState _state = SidecarState.NotStarted;
    private bool _lastHealthCheckSucceeded;
    private string? _lastError;
    private int _port;

    private Process? _process;
    private CancellationTokenSource? _supervisorCts;
    private Task? _supervisorTask;
    private TaskCompletionSource<bool>? _readyTcs;

    public SidecarHost(
        DataPathService paths,
        PythonRuntimeBootstrapper bootstrapper,
        ILogger<SidecarHost> logger,
        ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _bootstrapper = bootstrapper;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>HTTP base URL of the running sidecar (e.g. <c>http://127.0.0.1:54123</c>). Throws if not started.</summary>
    public string BaseUrl
    {
        get
        {
            lock (_stateLock)
            {
                if (_port == 0)
                    throw new InvalidOperationException("Sidecar is not yet listening; await EnsureReadyAsync first.");
                return $"http://127.0.0.1:{_port}";
            }
        }
    }

    /// <summary>Snapshot of sidecar status for the <c>/status</c> endpoint.</summary>
    public SidecarStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new SidecarStatus(_state, _lastHealthCheckSucceeded, _port, _lastError);
        }
    }

    /// <summary>
    /// Raised on the thread that mutates state (typically the supervisor task).
    /// Subscribers must marshal to the UI dispatcher themselves. The status
    /// footer view-model uses this to keep "Engine: {state}" live.
    /// </summary>
    public event EventHandler<SidecarStatus>? StateChanged;

    /// <summary>
    /// Wait until the sidecar reports healthy. Used by the engine before issuing
    /// the first synthesis request. Times out via <paramref name="ct"/>.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_stateLock)
        {
            if (_state == SidecarState.Running && _lastHealthCheckSucceeded) return;
            if (_state == SidecarState.Failed)
                throw new InvalidOperationException(_lastError ?? "Sidecar is in a failed state.");
            tcs = _readyTcs;
        }

        if (tcs is null)
            throw new InvalidOperationException("Sidecar is not started.");

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        await tcs.Task.ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_supervisorTask is not null)
                return Task.CompletedTask;
            _supervisorCts = new CancellationTokenSource();
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _supervisorTask = Task.Run(() => SuperviseAsync(_supervisorCts!.Token));
        _logger.LogInformation("SidecarHost supervisor started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        SetState(SidecarState.Stopping);
        try { _supervisorCts?.Cancel(); } catch { /* tolerate */ }

        try
        {
            if (_supervisorTask is not null)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                await Task.WhenAny(_supervisorTask, Task.Delay(Timeout.Infinite, timeoutCts.Token))
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }

        TerminateProcess();
        _logger.LogInformation("SidecarHost stopped.");
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        var sidecarLogger = _loggerFactory.CreateLogger("sidecar");
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                attempt++;
                if (!_bootstrapper.IsBootstrapped)
                {
                    SetFailed("Pocket-tts runtime is not bootstrapped — first-run dialog has not completed.");
                    return;
                }

                SetState(SidecarState.Starting);
                _port = 0;

                var psi = new ProcessStartInfo
                {
                    FileName = _bootstrapper.PythonExePath,
                    // -u forces unbuffered stdout/stderr so we observe Uvicorn's startup banner promptly.
                    Arguments = "-u -m pocket_tts serve --host 127.0.0.1 --port 0",
                    WorkingDirectory = _paths.PythonRuntimePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // Make sure HuggingFace cache lands in our LocalAppData (not the user's home), so
                // pocket-tts model weights live in the same place as everything else mockingbird owns.
                psi.Environment["HF_HOME"] = _paths.PocketTtsModelPath;
                psi.Environment["PYTHONIOENCODING"] = "utf-8";

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process = process;

                var portTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (_, e) => HandleSidecarLine(sidecarLogger, e.Data, portTcs, isErr: false);
                process.ErrorDataReceived += (_, e) => HandleSidecarLine(sidecarLogger, e.Data, portTcs, isErr: true);

                if (!process.Start())
                    throw new InvalidOperationException("Failed to spawn pocket-tts sidecar.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _logger.LogInformation("Pocket-tts sidecar started (PID {Pid}, attempt {Attempt})", process.Id, attempt);

                // Wait for the port to be reported (or for the process to die).
                using (var portTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    portTimeoutCts.CancelAfter(TimeSpan.FromMinutes(5)); // first launch may download weights
                    var port = await WaitForPortAsync(portTcs, process, portTimeoutCts.Token).ConfigureAwait(false);
                    lock (_stateLock) { _port = port; }
                    _logger.LogInformation("Pocket-tts sidecar listening on 127.0.0.1:{Port}", port);
                }

                // Health-check loop until we succeed or the process dies.
                using (var healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    healthCts.CancelAfter(TimeSpan.FromMinutes(5));
                    await PollHealthUntilReadyAsync(process, healthCts.Token).ConfigureAwait(false);
                }

                SetState(SidecarState.Running, healthy: true);
                _readyTcs?.TrySetResult(true);
                attempt = 0; // reset backoff once we got to running

                // Wait for the process to exit (clean shutdown or crash).
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested) break;

                _logger.LogWarning("Pocket-tts sidecar exited unexpectedly (code {Code}). Restarting…", process.ExitCode);
                SetState(SidecarState.Restarting, healthy: false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SidecarHost supervisor error on attempt {Attempt}", attempt);
                SetState(SidecarState.Restarting, healthy: false, error: ex.Message);
            }
            finally
            {
                TerminateProcess();
            }

            if (ct.IsCancellationRequested) break;

            // Back off: 1s, 2s, 4s, 8s, capped at 30s. After 5 attempts, fail permanently
            // so we don't pin a CPU re-spawning a broken interpreter forever.
            if (attempt >= 5)
            {
                SetFailed($"Sidecar failed to start after {attempt} attempts. Check logs.");
                return;
            }
            var delaySeconds = Math.Min(30, (int)Math.Pow(2, Math.Min(5, attempt - 1)));
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            // Reset the readiness signal so callers waiting on EnsureReadyAsync see the next attempt.
            lock (_stateLock)
            {
                _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        // Drain readiness waiters when shutting down.
        _readyTcs?.TrySetCanceled();
    }

    private async Task<int> WaitForPortAsync(TaskCompletionSource<int> portTcs, Process process, CancellationToken ct)
    {
        var exitedTask = process.WaitForExitAsync(ct);
        var portTask = portTcs.Task;
        var completed = await Task.WhenAny(portTask, exitedTask).ConfigureAwait(false);
        if (completed == exitedTask && process.HasExited)
        {
            throw new InvalidOperationException(
                $"Sidecar exited (code {process.ExitCode}) before reporting a port.");
        }
        return await portTask.ConfigureAwait(false);
    }

    private async Task PollHealthUntilReadyAsync(Process process, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var url = BaseUrl + "/health";
        while (!ct.IsCancellationRequested)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Sidecar exited (code {process.ExitCode}) before /health became ready.");
            try
            {
                using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    lock (_stateLock) { _lastHealthCheckSucceeded = true; _lastError = null; }
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
            {
                // Sidecar not yet accepting connections — keep polling.
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        ct.ThrowIfCancellationRequested();
    }

    private void HandleSidecarLine(ILogger sidecarLogger, string? line, TaskCompletionSource<int> portTcs, bool isErr)
    {
        if (string.IsNullOrEmpty(line)) return;

        // Uvicorn logs to stderr by default. Both streams flow into Serilog under the same source.
        if (isErr) sidecarLogger.LogInformation("{Line}", line);
        else sidecarLogger.LogInformation("{Line}", line);

        if (!portTcs.Task.IsCompleted)
        {
            var match = PortRegex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                portTcs.TrySetResult(port);
        }
    }

    private void TerminateProcess()
    {
        var process = _process;
        _process = null;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                // Try graceful close first; uvicorn handles SIGINT/CTRL-C, but on Windows
                // sending CTRL_BREAK to a child without a console is unreliable. Fall back to Kill.
                try { process.CloseMainWindow(); } catch { /* tolerate */ }
                if (!process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error terminating sidecar process.");
        }
        finally
        {
            try { process.Dispose(); } catch { /* tolerate */ }
        }
    }

    private void SetState(SidecarState state, bool healthy = false, string? error = null)
    {
        SidecarStatus snapshot;
        lock (_stateLock)
        {
            _state = state;
            _lastHealthCheckSucceeded = healthy;
            if (error is not null) _lastError = error;
            snapshot = new SidecarStatus(_state, _lastHealthCheckSucceeded, _port, _lastError);
        }
        RaiseStateChanged(snapshot);
    }

    private void SetFailed(string error)
    {
        SidecarStatus snapshot;
        lock (_stateLock)
        {
            _state = SidecarState.Failed;
            _lastError = error;
            _lastHealthCheckSucceeded = false;
            snapshot = new SidecarStatus(_state, _lastHealthCheckSucceeded, _port, _lastError);
        }
        _logger.LogError("Sidecar permanently failed: {Error}", error);
        RaiseStateChanged(snapshot);
        _readyTcs?.TrySetException(new InvalidOperationException(error));
    }

    private void RaiseStateChanged(SidecarStatus snapshot)
    {
        try
        {
            StateChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SidecarHost.StateChanged subscriber threw.");
        }
    }

    public void Dispose()
    {
        try { _supervisorCts?.Cancel(); } catch { /* tolerate */ }
        TerminateProcess();
        _supervisorCts?.Dispose();
    }
}
