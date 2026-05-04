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
    private readonly SpeakService _speakService;
    private readonly VoiceCatalog _voiceCatalog;
    private readonly SidecarHost? _sidecar;
    private readonly ILogger<SpeakServer> _logger;
    private readonly string _host;
    private readonly int _port;

    private WebApplication? _app;

    public SpeakServer(
        SpeakQueue queue,
        SpeakService speakService,
        VoiceCatalog voiceCatalog,
        ILogger<SpeakServer> logger,
        SidecarHost? sidecar = null,
        string host = "127.0.0.1",
        int port = 7223)
    {
        _queue = queue;
        _speakService = speakService;
        _voiceCatalog = voiceCatalog;
        _sidecar = sidecar;
        _logger = logger;
        _host = host;
        _port = port;
    }

    /// <summary>Host the Kestrel listener is bound to (loopback in v1).</summary>
    public string Host => _host;

    /// <summary>TCP port the Kestrel listener is bound to.</summary>
    public int Port => _port;

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

            // Same call site as the Speak page's Play command — main-013 (Q2)
            // routes both surfaces through SpeakService.
            var voiceId = string.IsNullOrWhiteSpace(body.Voice) ? "test-voice" : body.Voice!;
            var (requestId, position) = _speakService.Enqueue(body.Text!, voiceId);
            return Results.Accepted($"/status?id={requestId}",
                new SpeakAccepted(requestId, position));
        });

        app.MapPost("/stop", () =>
        {
            int dropped = _speakService.StopAndDrain();
            return Results.Ok(new { stopped = true, dropped });
        });

        app.MapGet("/voices", async (CancellationToken ct) =>
        {
            // Same call site as the Speak page's voice picker — main-013 (Q4)
            // routes both surfaces through VoiceCatalog so cloned voices added
            // by main-015 land in both places at once.
            var voices = await _voiceCatalog.ListAsync(ct);
            return Results.Ok(voices);
        });

        app.MapGet("/status", () =>
        {
            // Sidecar status: the real one when PocketTtsEngine is wired, a synthetic
            // "stub" reading when the stub engine is gating things behind
            // MOCKINGBIRD_USE_STUB_ENGINE=1.
            object sidecarPayload;
            if (_sidecar is not null)
            {
                var s = _sidecar.GetStatus();
                sidecarPayload = new
                {
                    state = s.State.ToString().ToLowerInvariant(),
                    healthy = s.Healthy,
                    port = s.Port,
                    lastError = s.LastError,
                };
            }
            else
            {
                sidecarPayload = new { state = "stub", healthy = true, port = 0, lastError = (string?)null };
            }

            return Results.Ok(new
            {
                playing = _queue.IsPlaying,
                queueLength = _queue.QueueLength,
                currentRequestId = _queue.CurrentRequestId,
                sidecar = sidecarPayload,
            });
        });

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
