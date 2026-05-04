using System.Text.Json.Serialization;

namespace Mockingbird.Services.Voices;

/// <summary>
/// Source of a cloned voice's sample audio. v1 ships <see cref="Mic"/> and
/// <see cref="Loopback"/> (main-025); <see cref="Import"/> is reserved for a
/// future task — backend supports it, no UI surfaces it yet.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VoiceSource
{
    Mic,
    Loopback,
    Import,
}

/// <summary>
/// Per-voice metadata persisted to <c>&lt;dataPath&gt;\voices\&lt;id&gt;\meta.json</c>.
/// Schema is locked at v1 per the main-015 schema-ratification block; readers
/// tolerate unknown fields and refuse to read <see cref="SchemaVersion"/> &gt; 1.
/// </summary>
public sealed record ClonedVoiceMeta
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "pocket-tts";

    [JsonPropertyName("pocketTtsVersion")]
    public string? PocketTtsVersion { get; init; }

    [JsonPropertyName("source")]
    public VoiceSource Source { get; init; } = VoiceSource.Mic;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sampleSeconds")]
    public int SampleSeconds { get; init; }

    /// <summary>Relative path inside the voice folder, typically <c>"sample.wav"</c>.</summary>
    [JsonPropertyName("samplePath")]
    public string? SamplePath { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Compact row in <c>library.json</c>. Mirrors a subset of <see cref="ClonedVoiceMeta"/>
/// so the catalog can populate the picker without reading N per-voice files.
/// </summary>
public sealed record ClonedVoiceIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "pocket-tts";

    [JsonPropertyName("source")]
    public VoiceSource Source { get; init; } = VoiceSource.Mic;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Backing JSON shape for <c>library.json</c>. Forward-compatible: future
/// readers ignore unknown fields and refuse to read <see cref="SchemaVersion"/> &gt; 1.
/// </summary>
public sealed record VoiceLibraryFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("voices")]
    public IReadOnlyList<ClonedVoiceIndexEntry> Voices { get; init; } = Array.Empty<ClonedVoiceIndexEntry>();
}

/// <summary>
/// Diff payload for <see cref="VoiceLibraryService.LibraryChanged"/>. Coarse for v1 —
/// consumers re-list the library on the event. The shape leaves room for future
/// fine-grained subscribers.
/// </summary>
public sealed record LibraryChangedArgs(
    IReadOnlyList<string> AddedIds,
    IReadOnlyList<string> RemovedIds);

/// <summary>
/// Thrown by <see cref="VoiceLibraryService.AddAsync"/> for input validation
/// failures (empty name, oversize, id-collision-with-built-in). Callers (the
/// HTTP API for main-025, the Voices page) translate to a 400 / inline error.
/// </summary>
public sealed class VoiceValidationException : Exception
{
    public VoiceValidationException(string message) : base(message) { }
}
