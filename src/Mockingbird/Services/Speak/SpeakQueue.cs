using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Tts;

namespace Mockingbird.Services.Speak;

/// <summary>
/// Per ADR 0007: a single-reader, multi-writer <see cref="Channel{T}"/> of
/// <see cref="SpeakRequest"/>s, with a long-running playback worker pulling
/// requests one at a time, calling the engine, and piping audio through
/// <see cref="AudioPlayer"/>. Per ADR 0004: stop drains the queue.
/// </summary>
public sealed class SpeakQueue : BackgroundService
{
    private readonly Channel<SpeakRequest> _channel = Channel.CreateUnbounded<SpeakRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ITtsEngine _engine;
    private readonly AudioPlayer _player;
    private readonly ILogger<SpeakQueue> _logger;

    private readonly ConcurrentDictionary<string, SpeakRequest> _inFlight = new();
    private SpeakRequest? _current;

    public SpeakQueue(ITtsEngine engine, AudioPlayer player, ILogger<SpeakQueue> logger)
    {
        _engine = engine;
        _player = player;
        _logger = logger;
    }

    /// <summary>Approximate count of pending requests (head + tail). Cheap and good enough for /status.</summary>
    public int QueueLength => _inFlight.Count;

    public bool IsPlaying => _player.IsPlaying;

    public string? CurrentRequestId => _current?.RequestId;

    /// <summary>Enqueue a request. Returns the queue position (1 = playing now after dequeue).</summary>
    public int Enqueue(SpeakRequest request)
    {
        _inFlight[request.RequestId] = request;
        if (!_channel.Writer.TryWrite(request))
        {
            _inFlight.TryRemove(request.RequestId, out _);
            throw new InvalidOperationException("Speak queue is closed.");
        }
        _logger.LogInformation("Enqueued speak request {RequestId} (voice={Voice}, chars={Chars}, queue={Queue})",
            request.RequestId, request.VoiceId, request.Text.Length, _inFlight.Count);
        return _inFlight.Count;
    }

    /// <summary>
    /// Stop current playback AND drain pending requests (ADR 0004 default).
    /// Returns the number of pending items that were dropped (excluding the
    /// currently-playing request, which is also stopped).
    /// </summary>
    public int StopAndDrain()
    {
        // Cancel current
        var current = _current;
        if (current is not null)
        {
            try { current.Cts.Cancel(); } catch (ObjectDisposedException) { /* already done */ }
        }

        // Drain pending
        int drained = 0;
        while (_channel.Reader.TryRead(out var pending))
        {
            try { pending.Cts.Cancel(); } catch (ObjectDisposedException) { /* same */ }
            _inFlight.TryRemove(pending.RequestId, out _);
            drained++;
        }

        // Belt-and-braces: cut the audio output even if the worker hasn't observed cancel yet.
        _player.Stop();

        _logger.LogInformation("StopAndDrain — dropped {Count} pending requests, current cancelled={HadCurrent}",
            drained, current is not null);
        return drained;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpeakQueue worker started.");
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var request))
                {
                    _current = request;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken, request.Cts.Token);

                    try
                    {
                        var chunks = _engine.StreamAsync(request.Text, request.VoiceId, linked.Token);
                        await _player.PlayAsync(_engine.OutputFormat, chunks, linked.Token).ConfigureAwait(false);
                        _logger.LogInformation("Speak request {RequestId} completed.", request.RequestId);
                    }
                    catch (OperationCanceledException) when (request.Cts.IsCancellationRequested)
                    {
                        _logger.LogInformation("Speak request {RequestId} cancelled.", request.RequestId);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // App shutting down — exit the loop.
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Speak request {RequestId} failed.", request.RequestId);
                    }
                    finally
                    {
                        _inFlight.TryRemove(request.RequestId, out _);
                        request.Cts.Dispose();
                        _current = null;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            _logger.LogInformation("SpeakQueue worker stopped.");
        }
    }
}
