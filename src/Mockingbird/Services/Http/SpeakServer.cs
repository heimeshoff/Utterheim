using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Speak;
using Mockingbird.Services.Tts;

namespace Mockingbird.Services.Http;

/// <summary>
/// Per ADR 0003: Kestrel-hosted minimal API on 127.0.0.1:7223.
/// Loopback-only; no auth in v1. Endpoints:
///
///   POST /speak  — enqueue
///   POST /stop   — stop+drain (ADR 0004)
///   GET  /voices — engine's voice list
///   GET  /status — playback + queue + sidecar health
///
/// Hosted as an <see cref="IHostedService"/> alongside the WPF app.
/// </summary>
public sealed class SpeakServer : IHostedService
{
    private readonly SpeakQueue _queue;
    private readonly ITtsEngine _engine;
    private readonly ILogger<SpeakServer> _logger;
    private readonly string _host;
    private readonly int _port;

    private WebApplication? _app;

    public SpeakServer(
        SpeakQueue queue,
        ITtsEngine engine,
        ILogger<SpeakServer> logger,
        string host = "127.0.0.1",
        int port = 7223)
    {
        _queue = queue;
        _engine = engine;
        _logger = logger;
        _host = host;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{_host}:{_port}");
        builder.Logging.ClearProviders();
        // Logging is handled by Serilog at the host level — Kestrel logs surface there too.

        var app = builder.Build();

        app.MapPost("/speak", (SpeakBody body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });

            var voiceId = string.IsNullOrWhiteSpace(body.Voice) ? "test-voice" : body.Voice!;
            var request = new SpeakRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Text = body.Text!,
                VoiceId = voiceId,
            };

            int position = _queue.Enqueue(request);
            return Results.Accepted($"/status?id={request.RequestId}",
                new SpeakAccepted(request.RequestId, position));
        });

        app.MapPost("/stop", () =>
        {
            int dropped = _queue.StopAndDrain();
            return Results.Ok(new { stopped = true, dropped });
        });

        app.MapGet("/voices", async (CancellationToken ct) =>
        {
            var voices = await _engine.ListVoicesAsync(ct);
            return Results.Ok(voices);
        });

        app.MapGet("/status", () => Results.Ok(new
        {
            playing = _queue.IsPlaying,
            queueLength = _queue.QueueLength,
            currentRequestId = _queue.CurrentRequestId,
            // Skeleton stub: there is no sidecar in main-009; main-011 wires the real one.
            sidecar = new { state = "stub", healthy = true },
        }));

        _app = app;

        try
        {
            await app.StartAsync(cancellationToken);
            _logger.LogInformation("SpeakServer listening on http://{Host}:{Port}", _host, _port);
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5 || ex.ErrorCode == 183)
        {
            _logger.LogError(ex, "Port {Port} appears to be in use. Override via settings.", _port);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpeakServer failed to start on http://{Host}:{Port}", _host, _port);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is null) return;
        try
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping SpeakServer.");
        }
        _app = null;
    }

    private sealed record SpeakBody(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("voice")] string? Voice);

    private sealed record SpeakAccepted(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("queuePosition")] int QueuePosition);
}
