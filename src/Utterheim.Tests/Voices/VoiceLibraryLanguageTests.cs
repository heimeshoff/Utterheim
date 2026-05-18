using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Utterheim.Services.Settings;
using Utterheim.Services.Voices;

namespace Utterheim.Tests.Voices;

/// <summary>
/// Coverage for main-040's voice-library schema change (ADR 0023): every
/// profile carries a <see cref="VoiceLanguage"/>, legacy on-disk files default
/// to English on load, and <see cref="VoiceLibraryService.AddAsync"/> persists
/// the language a clone was created with into both <c>meta.json</c> and the
/// master <c>library.json</c> index entry.
/// </summary>
public class VoiceLibraryLanguageTests
{
    /// <summary>
    /// JsonSerializerOptions matching the read path in
    /// <see cref="VoiceLibraryService"/>. Case-insensitive property matching
    /// is the load-bearing piece — without it `JsonStringEnumMemberName` lookup
    /// for the lower-case JSON value (<c>"english"</c>) would still resolve,
    /// but the field defaulting logic below depends on the same options the
    /// production code uses.
    /// </summary>
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// AC 5.a (legacy file → english): a <c>library.json</c> written before
    /// main-040 has no <c>language</c> field on its voice entries. ADR 0023's
    /// migration rule says every such entry loads as
    /// <see cref="VoiceLanguage.English"/>. We assert directly on the
    /// deserialised <see cref="VoiceLibraryFile"/> — the load path in
    /// <see cref="VoiceLibraryService.LoadAsync"/> uses these exact options on
    /// the same record type, so a green test here proves the schema migration
    /// without needing the filesystem.
    /// </summary>
    [Fact]
    public void LegacyLibraryJson_DefaultsLanguageToEnglish()
    {
        // Verbatim shape from main's README "Master library.json" example,
        // captured before main-040 added the language field.
        const string legacyJson = """
        {
          "schemaVersion": 1,
          "voices": [
            { "id": "marco", "name": "Marco", "engine": "pocket-tts",
              "source": "Mic", "createdAt": "2026-05-04T12:34:56+00:00" }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<VoiceLibraryFile>(legacyJson, JsonReadOptions);

        Assert.NotNull(loaded);
        var entry = Assert.Single(loaded!.Voices);
        Assert.Equal("marco", entry.Id);
        Assert.Equal(VoiceLanguage.English, entry.Language);
    }

    /// <summary>
    /// Complement to the library.json migration: pre-main-040 per-voice
    /// <c>meta.json</c> files also lack the field. <see cref="ClonedVoiceMeta"/>
    /// must default to English so the orphan-reinsertion path in
    /// <see cref="VoiceLibraryService.LoadAsync"/> rebuilds the index entry
    /// without losing the migration default.
    /// </summary>
    [Fact]
    public void LegacyClonedVoiceMeta_DefaultsLanguageToEnglish()
    {
        const string legacyMetaJson = """
        {
          "schemaVersion": 1,
          "id": "marco",
          "name": "Marco",
          "engine": "pocket-tts",
          "pocketTtsVersion": null,
          "source": "Mic",
          "createdAt": "2026-05-04T12:34:56+00:00",
          "sampleSeconds": 12,
          "samplePath": "sample.wav",
          "tags": []
        }
        """;

        var meta = JsonSerializer.Deserialize<ClonedVoiceMeta>(legacyMetaJson, JsonReadOptions);

        Assert.NotNull(meta);
        Assert.Equal(VoiceLanguage.English, meta!.Language);
    }

    /// <summary>
    /// AC 5.b (new voice persists its language): a clone created via
    /// <see cref="VoiceLibraryService.AddAsync"/> with
    /// <see cref="VoiceLanguage.German"/> writes <c>"language": "german"</c>
    /// into both <c>meta.json</c> and the matching <c>library.json</c> index
    /// row. We round-trip through real disk under a per-test temp folder so
    /// the assertion exercises the full Add → write → re-read path.
    /// </summary>
    [Fact]
    public async Task AddAsync_PersistsLanguage_OnBothMetaAndLibraryIndex()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        // Tiny non-empty buffer stands in for the .safetensors export — the
        // service only requires non-empty, it doesn't parse the bytes.
        var profileBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var meta = await library.AddAsync(
            displayName: "Hannah",
            source: VoiceSource.Mic,
            sampleSeconds: 8,
            profileBytes: profileBytes,
            sampleBytes: null,
            ct: CancellationToken.None,
            language: VoiceLanguage.German);

        Assert.Equal(VoiceLanguage.German, meta.Language);

        // meta.json on disk has the language field.
        var metaPath = Path.Combine(temp.Paths.VoicesPath, meta.Id, "meta.json");
        Assert.True(File.Exists(metaPath));
        var metaJson = File.ReadAllText(metaPath);
        Assert.Contains("\"language\"", metaJson);
        Assert.Contains("\"german\"", metaJson);

        // library.json on disk has the language field on the index row.
        var libraryJson = File.ReadAllText(temp.Paths.VoiceLibraryPath);
        Assert.Contains("\"language\"", libraryJson);
        Assert.Contains("\"german\"", libraryJson);

        // And the in-memory listing reflects it without a reload.
        var listed = await library.ListClonedAsync(CancellationToken.None);
        var row = Assert.Single(listed);
        Assert.Equal(VoiceLanguage.German, row.Language);
    }

    /// <summary>
    /// Backward-compat sanity for the optional-arg overload: a caller that
    /// doesn't supply a language gets English persisted. Protects pre-main-041
    /// callers (notably <see cref="Utterheim.ViewModels.Pages.VoiceCloningViewModel"/>'s
    /// existing save flow) from silently changing default.
    /// </summary>
    [Fact]
    public async Task AddAsync_WithoutLanguageArg_DefaultsToEnglish()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        var profileBytes = new byte[] { 0x01 };

        var meta = await library.AddAsync(
            displayName: "Marco",
            source: VoiceSource.Mic,
            sampleSeconds: 6,
            profileBytes: profileBytes,
            sampleBytes: null,
            ct: CancellationToken.None);

        Assert.Equal(VoiceLanguage.English, meta.Language);
    }
}

/// <summary>
/// Test fixture that backs <see cref="DataPathService"/> with a fresh temp
/// folder for the duration of one test, then deletes it on disposal. Uses
/// reflection to swap the private <c>_bootstrap</c> field so we don't pollute
/// the user's real <c>%APPDATA%\Utterheim\bootstrap.json</c> (which
/// <see cref="DataPathService.SetDataPath"/> would write to).
/// </summary>
internal sealed class TempDataPath : System.IDisposable
{
    public DataPathService Paths { get; }
    public string Root { get; }

    public TempDataPath()
    {
        Root = Path.Combine(Path.GetTempPath(), "Utterheim.Tests", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Paths = new DataPathService(NullLogger<DataPathService>.Instance);

        // Inject the temp root via the bootstrap config without touching the
        // user's real bootstrap.json on disk.
        var bootstrapField = typeof(DataPathService)
            .GetField("_bootstrap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new System.InvalidOperationException("DataPathService._bootstrap field not found.");
        bootstrapField.SetValue(Paths, new BootstrapConfig { DataPath = Root });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — a still-locked file in CI is not worth
            // failing the test on.
        }
    }
}
