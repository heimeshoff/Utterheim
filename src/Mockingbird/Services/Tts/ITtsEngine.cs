namespace Mockingbird.Services.Tts;

/// <summary>
/// The seam every TTS engine plugs into. v1 has one implementation
/// (StubTtsEngine, replaced by PocketTtsEngine in main-011) but the interface
/// is the single point future engines wire through.
///
/// Per ADR 0002:
/// - Implementations stream audio chunks as raw PCM bytes (sample format declared per engine).
/// - Voice IDs are opaque strings the engine resolves internally.
/// - Cancellation via the supplied token must abort in-flight synthesis promptly.
/// </summary>
public interface ITtsEngine
{
    /// <summary>Sample format the engine produces. Consumers configure NAudio accordingly.</summary>
    AudioFormat OutputFormat { get; }

    /// <summary>Built-in / available voices known to the engine.</summary>
    Task<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken ct);

    /// <summary>
    /// Stream synthesised audio for <paramref name="text"/> in <paramref name="voiceId"/>.
    /// Yields raw PCM byte chunks in <see cref="OutputFormat"/>. The first chunk should
    /// arrive within the engine's first-chunk-latency budget (≤2 s end-to-end target).
    /// </summary>
    IAsyncEnumerable<byte[]> StreamAsync(string text, string voiceId, CancellationToken ct);
}

/// <summary>Sample format declaration. Mirrors NAudio's WaveFormat fields.</summary>
public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample);

/// <summary>
/// One row of the engine's voice list. The shape matches the JSON the
/// <c>GET /voices</c> endpoint returns.
/// </summary>
public sealed record VoiceDescriptor(string Id, string Name, string Engine, bool IsBuiltIn);
