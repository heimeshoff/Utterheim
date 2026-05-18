using System.Text.Json.Serialization;

namespace Utterheim.Services.Voices;

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
/// Language a voice profile speaks. Per ADR 0023, each voice carries exactly
/// one language and the sidecar routes <c>/speak</c> to the matching resident
/// <c>TTSModel</c> based on it. v1 ships <see cref="English"/> + <see cref="German"/>;
/// pocket-tts 2.1.0 also has french / italian / spanish / portuguese models — adding
/// them is a one-line enum extension once a built-in voice or clone for the
/// language is needed (main-040 stops at en + de to match the preload decision
/// in ADR 0024). Persisted as a lower-case JSON string (<c>"english"</c>,
/// <c>"german"</c>) on both per-voice <c>meta.json</c> and the master
/// <c>library.json</c> entries. Legacy entries without the field default to
/// <see cref="English"/> on load (ADR 0023 migration rule).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceLanguage>))]
public enum VoiceLanguage
{
    [JsonStringEnumMemberName("english")]
    English,
    [JsonStringEnumMemberName("german")]
    German,
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

    /// <summary>
    /// Language this profile speaks (ADR 0023). Persisted as a lower-case JSON
    /// string. Legacy <c>meta.json</c> files written before main-040 lack this
    /// field; <see cref="System.Text.Json"/> applies the init default
    /// <see cref="VoiceLanguage.English"/>, which matches the ADR-prescribed
    /// migration rule.
    /// </summary>
    [JsonPropertyName("language")]
    public VoiceLanguage Language { get; init; } = VoiceLanguage.English;

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

    /// <summary>
    /// Language this profile speaks (ADR 0023). Mirrored from <see cref="ClonedVoiceMeta.Language"/>
    /// so the in-memory catalog and the sidecar's <c>voice_id → language</c> map
    /// (consumed by main-039's routing) can read the field without opening every
    /// per-voice <c>meta.json</c>. Legacy <c>library.json</c> rows without the
    /// field default to <see cref="VoiceLanguage.English"/>.
    /// </summary>
    [JsonPropertyName("language")]
    public VoiceLanguage Language { get; init; } = VoiceLanguage.English;

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
