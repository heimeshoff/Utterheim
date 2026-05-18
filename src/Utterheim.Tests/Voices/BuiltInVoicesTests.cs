using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Utterheim.Services.Tts;
using Utterheim.Services.Voices;

namespace Utterheim.Tests.Voices;

/// <summary>
/// Coverage for main-040's built-in voice list extension: the German voice
/// <c>juergen</c> (added to pocket-tts 2.1.0) is enumerated alongside the
/// existing Les-Misérables-derived English built-ins, and every row carries
/// the correct <see cref="VoiceLanguage"/> per ADR 0023.
/// </summary>
public class BuiltInVoicesTests
{
    /// <summary>
    /// AC 5.c — <c>juergen</c> appears in <see cref="PocketTtsEngine.ListVoicesAsync"/>
    /// with <see cref="VoiceLanguage.German"/>. main-039's sidecar voice→language
    /// map (consumes the same descriptor list) depends on this row to route
    /// speak requests for built-in German voices.
    ///
    /// <see cref="PocketTtsEngine.ListVoicesAsync"/> is pure — it returns a
    /// static list and never touches <see cref="SidecarHost"/> /
    /// <see cref="VoiceLibraryService"/> — so we hand it <c>null!</c> deps
    /// rather than wire a full DI graph for one assertion. If that ever
    /// changes the test will fail loudly (NullReferenceException in ctor)
    /// and the maintainer adjusts.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task PocketTtsEngine_EnumeratesJuergen_AsGerman()
    {
        var engine = new PocketTtsEngine(
            sidecar: null!,
            library: null!,
            logger: NullLogger<PocketTtsEngine>.Instance);

        var voices = await engine.ListVoicesAsync(CancellationToken.None);

        var juergen = voices.SingleOrDefault(v =>
            string.Equals(v.Id, "juergen", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(juergen);
        Assert.True(juergen!.IsBuiltIn);
        Assert.Equal("pocket-tts", juergen.Engine);
        Assert.Equal(VoiceLanguage.German, juergen.Language);
    }

    /// <summary>
    /// Companion check: the eight pre-existing English built-ins keep their
    /// <see cref="VoiceLanguage.English"/> tag. main-040 must not silently
    /// flip any of them while adding the German one.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task PocketTtsEngine_EnglishBuiltIns_KeepEnglishTag()
    {
        var engine = new PocketTtsEngine(
            sidecar: null!,
            library: null!,
            logger: NullLogger<PocketTtsEngine>.Instance);

        var voices = await engine.ListVoicesAsync(CancellationToken.None);

        string[] englishIds = { "alba", "marius", "javert", "jean", "fantine", "cosette", "eponine", "azelma" };
        foreach (var id in englishIds)
        {
            var voice = voices.SingleOrDefault(v =>
                string.Equals(v.Id, id, System.StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(voice);
            Assert.Equal(VoiceLanguage.English, voice!.Language);
        }
    }
}
