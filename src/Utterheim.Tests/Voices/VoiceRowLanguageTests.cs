using System;
using Utterheim.Services.Tts;
using Utterheim.Services.Voices;
using Utterheim.ViewModels.Pages;

namespace Utterheim.Tests.Voices;

/// <summary>
/// Coverage for main-041's per-row language surface: the Voices page list
/// shows each voice's <see cref="VoiceLanguage"/> next to its name (chip /
/// badge / column — the chosen presentation is a compact chip per the
/// task's styleguide note). The view-model exposes the language as both a
/// strongly-typed property (for converters / future filtering) and a
/// pre-formatted short label (<c>"EN"</c> / <c>"DE"</c>) so the XAML
/// template can bind a single <c>TextBlock</c>.
/// </summary>
public class VoiceRowLanguageTests
{
    /// <summary>
    /// AC 4 — a row built from an English <see cref="VoiceDescriptor"/>
    /// carries <see cref="VoiceLanguage.English"/> and a <c>"EN"</c> label.
    /// </summary>
    [Fact]
    public void EnglishBuiltIn_HasEnglishLanguage_AndEnLabel()
    {
        var descriptor = new VoiceDescriptor(
            Id: "alba", Name: "Alba", Engine: "pocket-tts",
            IsBuiltIn: true, Language: VoiceLanguage.English);

        var row = new VoiceRowViewModel(descriptor, previewAction: _ => { });

        Assert.Equal(VoiceLanguage.English, row.Language);
        Assert.Equal("EN", row.LanguageLabel);
    }

    /// <summary>
    /// AC 4 — a row built from a German <see cref="VoiceDescriptor"/>
    /// (juergen, the built-in added in main-040) carries
    /// <see cref="VoiceLanguage.German"/> and a <c>"DE"</c> label. This is the
    /// load-bearing case for the new badge.
    /// </summary>
    [Fact]
    public void GermanBuiltIn_HasGermanLanguage_AndDeLabel()
    {
        var descriptor = new VoiceDescriptor(
            Id: "juergen", Name: "Juergen", Engine: "pocket-tts",
            IsBuiltIn: true, Language: VoiceLanguage.German);

        var row = new VoiceRowViewModel(descriptor, previewAction: _ => { });

        Assert.Equal(VoiceLanguage.German, row.Language);
        Assert.Equal("DE", row.LanguageLabel);
    }

    /// <summary>
    /// AC 4 — a cloned-voice row inherits its language from the descriptor
    /// the catalog surfaces it as. The catalog maps
    /// <see cref="ClonedVoiceIndexEntry.Language"/> onto
    /// <see cref="VoiceDescriptor.Language"/>; this test only asserts that the
    /// row-VM carries it through unchanged.
    /// </summary>
    [Fact]
    public void GermanClone_HasGermanLanguage_AndDeLabel()
    {
        var descriptor = new VoiceDescriptor(
            Id: "hannah-abcd", Name: "Hannah", Engine: "pocket-tts",
            IsBuiltIn: false, Language: VoiceLanguage.German);

        var row = new VoiceRowViewModel(descriptor, previewAction: _ => { });

        Assert.Equal(VoiceLanguage.German, row.Language);
        Assert.Equal("DE", row.LanguageLabel);
    }
}
