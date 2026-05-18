using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Utterheim.Services.Tts;
using Utterheim.Services.Voices;
using Utterheim.Tests.Voices;

namespace Utterheim.Tests.Tts;

/// <summary>
/// Coverage for main-039: <see cref="PocketTtsEngine"/> sends each speak
/// request to the sidecar with the voice's language tagged on the wire so
/// the multi-model sidecar can route it to the matching resident
/// <see cref="Voices.VoiceLanguage"/> <c>TTSModel</c>. The Claude-Code-facing
/// HTTP contract <c>{text, voice}</c> from ADR 0003 stays unchanged — only
/// the C#-to-sidecar-internal call carries the routing field, materialised
/// as the <c>X-Voice-Language</c> header (see this task's Notes).
///
/// The engine's <see cref="PocketTtsEngine.BuildSpeakRequest"/> seam returns
/// the <see cref="HttpRequestMessage"/> it would send so we can assert
/// without spinning up an HTTP listener. End-to-end coverage of the request
/// actually flying lives in the manual smoke test (AC 6 of main-039).
/// </summary>
public class PocketTtsEngineLanguageRoutingTests
{
    private const string BaseUrl = "http://127.0.0.1:54321";

    /// <summary>
    /// AC 3 — built-in English voice "alba" tags the outgoing request with
    /// <c>X-Voice-Language: english</c>. The sidecar's middleware reads that
    /// header and routes the call to the English resident model.
    /// </summary>
    [Fact]
    public void BuildSpeakRequest_BuiltInEnglishVoice_TagsHeaderEnglish()
    {
        var engine = NewEngineWithoutLibrary();

        var built = engine.BuildSpeakRequest(BaseUrl, "Hello world.", "alba");
        try
        {
            Assert.Equal($"{BaseUrl}/tts", built.Request.RequestUri!.ToString());
            Assert.True(built.Request.Headers.TryGetValues("X-Voice-Language", out var values));
            Assert.Equal("english", Assert.Single(values!));
        }
        finally
        {
            built.Request.Dispose();
            built.ContentDisposable.Dispose();
        }
    }

    /// <summary>
    /// AC 3 — built-in German voice "juergen" (added in main-040) tags the
    /// outgoing request with <c>X-Voice-Language: german</c>. This is the
    /// load-bearing case for the new German routing path.
    /// </summary>
    [Fact]
    public void BuildSpeakRequest_BuiltInGermanVoice_TagsHeaderGerman()
    {
        var engine = NewEngineWithoutLibrary();

        var built = engine.BuildSpeakRequest(BaseUrl, "Hallo Welt.", "juergen");
        try
        {
            Assert.Equal($"{BaseUrl}/tts", built.Request.RequestUri!.ToString());
            Assert.True(built.Request.Headers.TryGetValues("X-Voice-Language", out var values));
            Assert.Equal("german", Assert.Single(values!));
        }
        finally
        {
            built.Request.Dispose();
            built.ContentDisposable.Dispose();
        }
    }

    /// <summary>
    /// AC 3 — cloned voice's language comes from
    /// <see cref="VoiceLibraryService"/> (via the new
    /// <see cref="VoiceLibraryService.TryResolveLanguage"/> seam). A clone
    /// added with <see cref="VoiceLanguage.German"/> rides the same header.
    /// </summary>
    [Fact]
    public async Task BuildSpeakRequest_ClonedGermanVoice_TagsHeaderGerman()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        var meta = await library.AddAsync(
            displayName: "Hannah",
            source: VoiceSource.Mic,
            sampleSeconds: 8,
            profileBytes: new byte[] { 0x01, 0x02, 0x03 },
            sampleBytes: null,
            ct: CancellationToken.None,
            language: VoiceLanguage.German);

        var engine = new PocketTtsEngine(
            sidecar: null!,
            library: library,
            logger: NullLogger<PocketTtsEngine>.Instance);

        var built = engine.BuildSpeakRequest(BaseUrl, "Hallo Hannah.", meta.Id);
        try
        {
            Assert.Equal($"{BaseUrl}/tts-with-state", built.Request.RequestUri!.ToString());
            Assert.True(built.Request.Headers.TryGetValues("X-Voice-Language", out var values));
            Assert.Equal("german", Assert.Single(values!));
        }
        finally
        {
            built.Request.Dispose();
            built.ContentDisposable.Dispose();
        }
    }

    /// <summary>
    /// AC 3 — cloned voice with default language English rides the
    /// <c>english</c> tag. Guards against the engine accidentally defaulting
    /// every clone to the first language in the dict.
    /// </summary>
    [Fact]
    public async Task BuildSpeakRequest_ClonedEnglishVoice_TagsHeaderEnglish()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        var meta = await library.AddAsync(
            displayName: "Marco",
            source: VoiceSource.Mic,
            sampleSeconds: 6,
            profileBytes: new byte[] { 0x01 },
            sampleBytes: null,
            ct: CancellationToken.None);

        var engine = new PocketTtsEngine(
            sidecar: null!,
            library: library,
            logger: NullLogger<PocketTtsEngine>.Instance);

        var built = engine.BuildSpeakRequest(BaseUrl, "Hello Marco.", meta.Id);
        try
        {
            Assert.Equal($"{BaseUrl}/tts-with-state", built.Request.RequestUri!.ToString());
            Assert.True(built.Request.Headers.TryGetValues("X-Voice-Language", out var values));
            Assert.Equal("english", Assert.Single(values!));
        }
        finally
        {
            built.Request.Dispose();
            built.ContentDisposable.Dispose();
        }
    }

    /// <summary>
    /// AC: unknown voice id (no built-in match, no library entry) throws
    /// <see cref="System.InvalidOperationException"/> before any HTTP machinery
    /// is touched. This is the same shape the engine threw before main-039;
    /// the language-routing refactor must not regress it.
    /// </summary>
    [Fact]
    public async Task BuildSpeakRequest_UnknownVoice_Throws()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        var engine = new PocketTtsEngine(
            sidecar: null!,
            library: library,
            logger: NullLogger<PocketTtsEngine>.Instance);

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            engine.BuildSpeakRequest(BaseUrl, "Hello.", "doesnotexist"));
        Assert.Contains("doesnotexist", ex.Message);
    }

    /// <summary>
    /// AC sanity: <see cref="VoiceLibraryService.TryResolveLanguage"/> returns
    /// the language a cloned voice was added with, and null for unknown ids.
    /// </summary>
    [Fact]
    public async Task TryResolveLanguage_ReturnsAddedLanguageOrNull()
    {
        using var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        await library.LoadAsync(CancellationToken.None);

        var meta = await library.AddAsync(
            displayName: "Hannah",
            source: VoiceSource.Mic,
            sampleSeconds: 8,
            profileBytes: new byte[] { 0x01 },
            sampleBytes: null,
            ct: CancellationToken.None,
            language: VoiceLanguage.German);

        Assert.Equal(VoiceLanguage.German, library.TryResolveLanguage(meta.Id));
        Assert.Null(library.TryResolveLanguage("nope-not-here"));
    }

    private static PocketTtsEngine NewEngineWithoutLibrary()
    {
        // Built-in lookup never touches VoiceLibraryService; pass null! and rely
        // on the engine to short-circuit on the BuiltInIds match. If a future
        // refactor changes that, NullReferenceException points the maintainer
        // straight at the regression.
        return new PocketTtsEngine(
            sidecar: null!,
            library: null!,
            logger: NullLogger<PocketTtsEngine>.Instance);
    }
}
