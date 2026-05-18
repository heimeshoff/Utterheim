using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Tts;

namespace Utterheim.Services.Voices;

/// <summary>
/// Thin C# wrapper around the sidecar's <c>POST /export-voice</c> endpoint
/// (per ADR 0015). Hands a WAV up to the resident pocket-tts model and
/// returns the <c>.safetensors</c> bytes of the exported voice profile.
///
/// Ownership: the sidecar is stateless between requests. Persisting the
/// returned bytes to <c>&lt;dataPath&gt;\voices\&lt;id&gt;\profile.safetensors</c>
/// is the C# host's job (<see cref="VoiceLibraryService.AddAsync"/>).
/// </summary>
public sealed class VoiceCloningClient
{
    private readonly SidecarHost _sidecar;
    private readonly ILogger<VoiceCloningClient> _logger;
    private readonly HttpClient _http;

    public VoiceCloningClient(SidecarHost sidecar, ILogger<VoiceCloningClient> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
        // Mimi audio-prompt encoding for a 5-30 s sample is ~1-2 s on CPU,
        // but first-clone-after-warm may also need to demand-load any extra
        // weights torchaudio pulls. Generous timeout matches PocketTtsEngine.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>
    /// Upload <paramref name="wavBytes"/> as the cloning sample and receive the
    /// exported <c>.safetensors</c> bytes back. Caller persists.
    /// </summary>
    /// <param name="wavBytes">Raw bytes of a RIFF WAV the sidecar can decode.</param>
    /// <param name="voiceId">Optional id for sidecar-side logging only — not persisted server-side.</param>
    /// <param name="language">
    /// Target pocket-tts language for the clone (main-041 / ADR 0023). The
    /// sidecar's <c>LanguageRoutingMiddleware</c> reads the
    /// <c>X-Voice-Language</c> header and swaps <c>pocket_tts.main.tts_model</c>
    /// to the matching resident model before <c>/export-voice</c>'s handler
    /// runs <c>get_state_for_audio_prompt</c>. Without the swap, a German
    /// clone would be encoded by whatever model the previous request happened
    /// to swap in (typically the English default).
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public async Task<byte[]> ExportVoiceAsync(
        ReadOnlyMemory<byte> wavBytes,
        string? voiceId,
        VoiceLanguage language,
        CancellationToken ct)
    {
        if (wavBytes.IsEmpty)
            throw new ArgumentException("wavBytes is empty.", nameof(wavBytes));

        await _sidecar.EnsureReadyAsync(ct).ConfigureAwait(false);

        var url = _sidecar.BaseUrl + "/export-voice";

        // ReadOnlyMemory<byte> → MemoryStream backed by a defensive copy so the
        // HttpContent owns the buffer for the request lifetime.
        var copy = wavBytes.ToArray();
        using var content = new MultipartFormDataContent();
        var wavContent = new ByteArrayContent(copy);
        // The Python side reads voice_wav as an UploadFile — give it a filename
        // hint so torchaudio's WAV backend dispatches correctly.
        content.Add(wavContent, "voice_wav", "sample.wav");
        if (!string.IsNullOrWhiteSpace(voiceId))
            content.Add(new StringContent(voiceId), "voice_id");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        // X-Voice-Language: english | german — same wire literal the speak
        // path uses (see PocketTtsEngine.LanguageWireValue). Stamped on every
        // request so the sidecar's middleware doesn't have to fall back to
        // its default-language slot.
        request.Headers.Add("X-Voice-Language", PocketTtsEngine.LanguageWireValue(language));

        _logger.LogInformation(
            "VoiceCloningClient: posting {Bytes}-byte WAV to /export-voice (voice='{Voice}', language={Language})",
            copy.Length, voiceId ?? "<unnamed>", language);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"/export-voice returned {(int)response.StatusCode}: {body}");
        }

        // Buffer the full body — .safetensors for a single voice profile is
        // small (a few hundred KB at most for a 100 M-param Mimi kvcache),
        // streaming round-trip would only complicate the caller.
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        _logger.LogInformation(
            "VoiceCloningClient: received {Bytes}-byte profile for voice '{Voice}'",
            bytes.Length, voiceId ?? "<unnamed>");
        return bytes;
    }
}
