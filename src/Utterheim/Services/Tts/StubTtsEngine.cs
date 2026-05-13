using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Utterheim.Services.Tts;

/// <summary>
/// Skeleton-only engine: synthesises a 440 Hz sine wave (A4) for ~1 second per
/// speak request, regardless of text content. Exists so the walking skeleton
/// can prove HTTP → queue → audio out works end-to-end without the
/// ~700 MB pocket-tts bootstrap. The real engine (PocketTtsEngine) lands in
/// main-011 and slots into the same <see cref="ITtsEngine"/> seam.
///
/// Audio is yielded in 20 ms chunks to exercise the streaming path.
/// </summary>
public sealed class StubTtsEngine : ITtsEngine
{
    private readonly ILogger<StubTtsEngine> _logger;

    public StubTtsEngine(ILogger<StubTtsEngine> logger)
    {
        _logger = logger;
    }

    public AudioFormat OutputFormat { get; } = new(SampleRate: 24000, Channels: 1, BitsPerSample: 16);

    public Task<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken ct)
    {
        // Hardcoded per orchestrator scope amendment for main-009.
        IReadOnlyList<VoiceDescriptor> voices =
        [
            new VoiceDescriptor(Id: "test-voice", Name: "Test Tone", Engine: "stub", IsBuiltIn: true),
        ];
        return Task.FromResult(voices);
    }

    public async IAsyncEnumerable<byte[]> StreamAsync(
        string text,
        string voiceId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("StubTtsEngine synthesising {Chars} chars for voice '{Voice}' (test tone)",
            text?.Length ?? 0, voiceId);

        const double frequency = 440.0;
        const double durationSeconds = 1.0;
        const double chunkSeconds = 0.02; // 20 ms
        int sampleRate = OutputFormat.SampleRate;
        int totalSamples = (int)(sampleRate * durationSeconds);
        int chunkSamples = (int)(sampleRate * chunkSeconds);

        double phase = 0.0;
        double phaseStep = 2.0 * Math.PI * frequency / sampleRate;

        for (int produced = 0; produced < totalSamples; produced += chunkSamples)
        {
            ct.ThrowIfCancellationRequested();

            int n = Math.Min(chunkSamples, totalSamples - produced);
            byte[] chunk = new byte[n * 2]; // 16-bit PCM = 2 bytes per sample
            for (int i = 0; i < n; i++)
            {
                short pcm = (short)(Math.Sin(phase) * 0.3 * short.MaxValue);
                chunk[i * 2] = (byte)(pcm & 0xFF);
                chunk[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
                phase += phaseStep;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
            }

            yield return chunk;

            // Pace the stream so consumers can observe streaming behaviour.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(chunkSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
