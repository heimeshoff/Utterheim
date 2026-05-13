using System.IO;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Tts;
using NAudio.Wave;

namespace Utterheim.Services.Speak;

/// <summary>
/// Coarse playback status surfaced by <see cref="SpeakService.StatusChanged"/>.
/// Drives the Speak page's status line (main-013, Q6).
/// </summary>
public enum SpeakStatusKind
{
    /// <summary>No request in flight, queue empty.</summary>
    Idle,
    /// <summary>Request dequeued, engine producing audio, but no PCM has hit the device yet.</summary>
    Synthesising,
    /// <summary>Audio is actively playing on the output device.</summary>
    Playing,
    /// <summary>Transient label set by <see cref="SpeakService.StopAndDrain"/>; auto-clears to <see cref="Idle"/>.</summary>
    Stopped,
}

/// <summary>
/// Snapshot of the current speak status, including the active voice when one is set.
/// </summary>
public sealed record SpeakStatus(SpeakStatusKind Kind, string? VoiceId)
{
    public static readonly SpeakStatus Idle = new(SpeakStatusKind.Idle, null);
    public static readonly SpeakStatus Stopped = new(SpeakStatusKind.Stopped, null);
}

/// <summary>
/// In-process seam (main-013, Q2) shared by the HTTP endpoints and the Speak page.
/// Owns request construction (Guid, voice fallback, validation), surfaces the
/// status state-machine, and provides the off-queue render path for Save (Q5).
///
/// Listens to <see cref="SpeakQueue.RequestStarted"/> / <see cref="SpeakQueue.RequestCompleted"/>
/// plus polls <see cref="AudioPlayer.IsPlaying"/> at a low rate to compute status.
/// </summary>
public sealed class SpeakService : IDisposable
{
    private const int StoppedAutoClearMs = 2000;
    private const int PlayingPollIntervalMs = 100;

    private readonly SpeakQueue _queue;
    private readonly AudioPlayer _player;
    private readonly ITtsEngine _engine;
    private readonly ILogger<SpeakService> _logger;

    private SpeakStatus _current = SpeakStatus.Idle;
    private SpeakRequest? _activeRequest;
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _stoppedClearCts;
    private readonly object _stateLock = new();

    public SpeakService(
        SpeakQueue queue,
        AudioPlayer player,
        ITtsEngine engine,
        ILogger<SpeakService> logger)
    {
        _queue = queue;
        _player = player;
        _engine = engine;
        _logger = logger;

        _queue.RequestStarted += OnRequestStarted;
        _queue.RequestCompleted += OnRequestCompleted;
    }

    /// <summary>Latest snapshot of the speak status. Useful for late page navigations.</summary>
    public SpeakStatus CurrentStatus
    {
        get { lock (_stateLock) return _current; }
    }

    /// <summary>Fired whenever the speak status transitions.</summary>
    public event EventHandler<SpeakStatus>? StatusChanged;

    /// <summary>
    /// Build a <see cref="SpeakRequest"/> from raw inputs and enqueue it. Same code
    /// path the HTTP <c>POST /speak</c> endpoint uses — keeps the page and the API
    /// honest about routing.
    /// </summary>
    public (string requestId, int queuePosition) Enqueue(string text, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("text is required", nameof(text));

        var resolvedVoice = string.IsNullOrWhiteSpace(voiceId) ? "alba" : voiceId.Trim();
        var request = new SpeakRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Text = text,
            VoiceId = resolvedVoice,
        };
        var position = _queue.Enqueue(request);
        return (request.RequestId, position);
    }

    /// <summary>
    /// Stop current playback AND drain the queue (per ADR 0004). Surfaces the
    /// transient <see cref="SpeakStatusKind.Stopped"/> label for ~2 s, then
    /// returns to <see cref="SpeakStatusKind.Idle"/>.
    /// </summary>
    public int StopAndDrain()
    {
        var dropped = _queue.StopAndDrain();
        SetStatus(SpeakStatus.Stopped);
        ScheduleStoppedAutoClear();
        return dropped;
    }

    /// <summary>
    /// Render the supplied text to a <c>.wav</c> file at <paramref name="filePath"/>
    /// off-queue. Does NOT enqueue, does NOT touch <see cref="AudioPlayer"/> — Save
    /// must never interrupt a live playback request (main-013, Q5).
    /// </summary>
    public async Task RenderToFileAsync(string text, string voiceId, string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("text is required", nameof(text));
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is required", nameof(filePath));

        var resolvedVoice = string.IsNullOrWhiteSpace(voiceId) ? "alba" : voiceId.Trim();
        var format = _engine.OutputFormat;
        var waveFormat = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var writer = new WaveFileWriter(filePath, waveFormat);
        await foreach (var chunk in _engine.StreamAsync(text, resolvedVoice, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (chunk.Length == 0) continue;
            await writer.WriteAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
        }
        await writer.FlushAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("RenderToFileAsync wrote {Path} ({Voice}, {Bytes} bytes)",
            filePath, resolvedVoice, writer.Length);
    }

    private void OnRequestStarted(object? sender, SpeakRequest request)
    {
        lock (_stateLock)
        {
            _activeRequest = request;
        }
        SetStatus(new SpeakStatus(SpeakStatusKind.Synthesising, request.VoiceId));
        StartPlayingPoll(request);
    }

    private void OnRequestCompleted(object? sender, SpeakRequest request)
    {
        StopPlayingPoll();
        lock (_stateLock)
        {
            _activeRequest = null;
        }
        // Only flip to Idle if a Stop didn't just take us to Stopped (which has
        // its own timer-based clear). The state-machine spec calls Idle the
        // resting state once nothing is _current.
        if (_current.Kind != SpeakStatusKind.Stopped)
            SetStatus(SpeakStatus.Idle);
    }

    private void StartPlayingPoll(SpeakRequest request)
    {
        StopPlayingPoll();
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    if (_player.IsPlaying)
                    {
                        // First PCM chunk has hit the buffer — surface the playing label.
                        SpeakRequest? active;
                        lock (_stateLock) active = _activeRequest;
                        if (active is null) return;
                        SetStatus(new SpeakStatus(SpeakStatusKind.Playing, active.VoiceId));
                        return;
                    }
                    await Task.Delay(PlayingPollIntervalMs, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on completion / next request */ }
            catch (Exception ex) { _logger.LogWarning(ex, "Status poll loop threw."); }
        });
    }

    private void StopPlayingPoll()
    {
        var cts = Interlocked.Exchange(ref _pollCts, null);
        if (cts is null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private void ScheduleStoppedAutoClear()
    {
        // Cancel any prior pending clear so a flurry of Stops resets the timer.
        var prev = Interlocked.Exchange(ref _stoppedClearCts, null);
        if (prev is not null)
        {
            try { prev.Cancel(); } catch (ObjectDisposedException) { }
            prev.Dispose();
        }

        var cts = new CancellationTokenSource();
        _stoppedClearCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StoppedAutoClearMs, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested) return;
                // Only clear to Idle if we're still in Stopped (a new request may
                // have raced in and moved us to Synthesising/Playing).
                if (_current.Kind == SpeakStatusKind.Stopped)
                    SetStatus(SpeakStatus.Idle);
            }
            catch (OperationCanceledException) { /* expected */ }
        });
    }

    private void SetStatus(SpeakStatus next)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = !Equals(_current, next);
            _current = next;
        }
        if (!changed) return;
        try { StatusChanged?.Invoke(this, next); }
        catch (Exception ex) { _logger.LogWarning(ex, "StatusChanged handler threw."); }
    }

    public void Dispose()
    {
        _queue.RequestStarted -= OnRequestStarted;
        _queue.RequestCompleted -= OnRequestCompleted;
        StopPlayingPoll();
        var prev = Interlocked.Exchange(ref _stoppedClearCts, null);
        if (prev is not null)
        {
            try { prev.Cancel(); } catch (ObjectDisposedException) { }
            prev.Dispose();
        }
    }
}
