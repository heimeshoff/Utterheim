using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Mockingbird.Services.Tts;

/// <summary>
/// Real <see cref="ITtsEngine"/> backed by the pocket-tts Python sidecar (ADR 0002).
///
/// Synthesis flow (per <c>pocket_tts/main.py</c>):
///  - POST <c>/tts</c> with form-encoded <c>text</c> and <c>voice_url</c> (built-in voice name).
///  - Server returns a <c>StreamingResponse</c>: WAV header followed by 16-bit mono PCM at the
///    model's sample rate (24 kHz for the English models).
///  - We strip the WAV header on the way through and yield raw PCM chunks straight to
///    <see cref="Speak.AudioPlayer"/> — no buffering of the whole utterance, so first audio
///    arrives well within the 2 s end-to-end latency budget.
/// </summary>
public sealed class PocketTtsEngine : ITtsEngine
{
    // Pocket-tts ships eight built-in voices with the predefined-voice mapping in
    // pocket_tts/utils/utils.py. We surface this fixed subset for v1; voice cloning
    // (alongside extra voices like anna/vera/charles/etc.) is a separate task per
    // task main-011's "out of scope".
    private static readonly IReadOnlyList<VoiceDescriptor> BuiltInVoices =
    [
        new("alba",    "Alba",    "pocket-tts", true),
        new("marius",  "Marius",  "pocket-tts", true),
        new("javert",  "Javert",  "pocket-tts", true),
        new("jean",    "Jean",    "pocket-tts", true),
        new("fantine", "Fantine", "pocket-tts", true),
        new("cosette", "Cosette", "pocket-tts", true),
        new("eponine", "Eponine", "pocket-tts", true),
        new("azelma",  "Azelma",  "pocket-tts", true),
    ];

    // Default English models all use 24 kHz mimi sample rate, mono, 16-bit.
    // (Verified by reading pocket_tts/config/english.yaml + data/audio.py StreamingWAVWriter.)
    public AudioFormat OutputFormat { get; } = new(SampleRate: 24000, Channels: 1, BitsPerSample: 16);

    private readonly SidecarHost _sidecar;
    private readonly ILogger<PocketTtsEngine> _logger;
    private readonly HttpClient _http;

    public PocketTtsEngine(SidecarHost sidecar, ILogger<PocketTtsEngine> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
        // Long timeout — synthesis of long passages can take ~1/6 of audio length on CPU.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public Task<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken ct)
    {
        return Task.FromResult(BuiltInVoices);
    }

    public async IAsyncEnumerable<byte[]> StreamAsync(
        string text,
        string voiceId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var voice = string.IsNullOrWhiteSpace(voiceId) ? "alba" : voiceId.Trim();

        await _sidecar.EnsureReadyAsync(ct).ConfigureAwait(false);

        var url = _sidecar.BaseUrl + "/tts";
        using var content = new MultipartFormDataContent
        {
            { new StringContent(text), "text" },
            { new StringContent(voice), "voice_url" },
        };

        _logger.LogInformation("PocketTtsEngine synthesising {Chars} chars for voice '{Voice}'",
            text.Length, voice);

        // ResponseHeadersRead is load-bearing: the default ResponseContentRead buffers the
        // entire WAV before the awaited Task returns, throwing away the StreamingResponse on
        // the Python side. Per main-023's diagnosis (H4 confirmed), this is the difference
        // between input-length-independent ~200–500 ms first audio and ~20 ms/char buffering.
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Pocket-tts /tts returned {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Strip the WAV header. Standard PCM RIFF header is 44 bytes (RIFF/WAVE/fmt /data).
        // The sidecar's StreamingWAVWriter writes a `wave` module header which is 44 bytes for
        // mono 16-bit PCM. We could parse it formally, but pocket-tts always emits this exact
        // shape — we sanity-check the magic bytes and skip 44.
        await SkipWavHeaderAsync(stream, ct).ConfigureAwait(false);

        var buffer = new byte[8192];
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            // Copy out — caller may hold the chunk past the next read.
            var chunk = new byte[n];
            Buffer.BlockCopy(buffer, 0, chunk, 0, n);
            yield return chunk;
        }
    }

    private static async Task SkipWavHeaderAsync(Stream stream, CancellationToken ct)
    {
        // Read enough to cover RIFF + fmt + the start of `data` chunk header. 44 bytes is the
        // standard layout for 16-bit mono PCM written by Python's `wave` module.
        const int HeaderSize = 44;
        var header = new byte[HeaderSize];
        int read = 0;
        while (read < HeaderSize)
        {
            int got = await stream.ReadAsync(header.AsMemory(read, HeaderSize - read), ct).ConfigureAwait(false);
            if (got <= 0)
                throw new InvalidOperationException("Pocket-tts response ended before WAV header was complete.");
            read += got;
        }

        // Sanity check: RIFF....WAVE....data
        if (header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F'
            || header[8] != 'W' || header[9] != 'A' || header[10] != 'V' || header[11] != 'E')
        {
            throw new InvalidOperationException("Pocket-tts response did not start with a RIFF/WAVE header.");
        }
    }
}
