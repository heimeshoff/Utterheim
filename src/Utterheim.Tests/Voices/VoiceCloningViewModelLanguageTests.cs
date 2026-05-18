using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Utterheim.Services.Audio;
using Utterheim.Services.Voices;
using Utterheim.ViewModels.Pages;

namespace Utterheim.Tests.Voices;

/// <summary>
/// Coverage for main-041 view-model wiring: the clone card exposes a
/// <see cref="VoiceLanguage"/> picker, defaults to <see cref="VoiceLanguage.English"/>,
/// and the German-selected case hides the Rainbow Passage reading prompt
/// (the English-only prompt added in main-034). The Save flow forwards the
/// chosen language to <see cref="VoiceLibraryService.AddAsync"/>; the wider
/// save path is exercised by an integration test that uses the real
/// <see cref="TempDataPath"/> harness from <see cref="VoiceLibraryLanguageTests"/>.
///
/// The XAML picker itself can't be unit-tested headlessly — these tests cover
/// every binding source the view consumes.
/// </summary>
public class VoiceCloningViewModelLanguageTests
{
    /// <summary>
    /// AC 1 — the clone-flow VM exposes <see cref="VoiceLanguage.English"/> by
    /// default. The XAML picker binds to <see cref="VoiceCloningViewModel.Language"/>;
    /// a default of English matches Marco's "default will always be English"
    /// product decision recorded in main-041's task description.
    /// </summary>
    [Fact]
    public void Language_DefaultsToEnglish()
    {
        var vm = NewCloningVm();
        Assert.Equal(VoiceLanguage.English, vm.Language);
    }

    /// <summary>
    /// AC 1 — the picker has two options, English and German, in that order
    /// (English first so default-selection is also default-displayed). v1 ships
    /// only these two per ADR 0023; adding more is an enum-extension + this
    /// list change.
    /// </summary>
    [Fact]
    public void Languages_ContainsEnglishAndGerman_InOrder()
    {
        var vm = NewCloningVm();
        var langs = (IEnumerable<VoiceLanguage>)vm.Languages;
        Assert.Collection(langs,
            l => Assert.Equal(VoiceLanguage.English, l),
            l => Assert.Equal(VoiceLanguage.German, l));
    }

    /// <summary>
    /// AC 2 — when the picker is set to German, the Rainbow Passage block is
    /// hidden. We expose this as <see cref="VoiceCloningViewModel.IsRainbowPassageVisible"/>
    /// so the XAML can bind a single flag rather than chain two converters.
    /// The English-Mic case keeps the prompt visible (unchanged from main-034);
    /// the System-Audio case keeps it collapsed (unchanged from main-034); the
    /// German-Mic case is the new gate this task adds.
    /// </summary>
    [Fact]
    public void IsRainbowPassageVisible_HiddenWhenGermanSelected_EvenInMicMode()
    {
        var vm = NewCloningVm();
        // Pre-condition: English + Mic should show the prompt.
        Assert.Equal(VoiceLanguage.English, vm.Language);
        Assert.True(vm.IsMicMode);
        Assert.True(vm.IsRainbowPassageVisible);

        vm.Language = VoiceLanguage.German;

        Assert.False(vm.IsRainbowPassageVisible);
    }

    /// <summary>
    /// AC 2 — toggling back to English re-shows the Rainbow Passage block.
    /// Guards against an accidental one-way flag.
    /// </summary>
    [Fact]
    public void IsRainbowPassageVisible_RestoredWhenSwitchedBackToEnglish()
    {
        var vm = NewCloningVm();
        vm.Language = VoiceLanguage.German;
        Assert.False(vm.IsRainbowPassageVisible);

        vm.Language = VoiceLanguage.English;
        Assert.True(vm.IsRainbowPassageVisible);
    }

    /// <summary>
    /// Companion to main-034's visibility rule: System-Audio + English keeps
    /// the prompt collapsed (the user is not speaking, so the prompt is
    /// irrelevant). This test pins the existing behaviour while the German
    /// language gate is added — main-041 must not regress main-034's contract.
    /// </summary>
    [Fact]
    public void IsRainbowPassageVisible_CollapsedInSystemAudioMode_RegardlessOfLanguage()
    {
        var vm = NewCloningVm();
        vm.SelectedSource = CloningSource.SystemAudio;
        Assert.False(vm.IsRainbowPassageVisible);

        vm.Language = VoiceLanguage.German;
        Assert.False(vm.IsRainbowPassageVisible);
    }

    // ------------------------------------------------------------------
    // main-042 — German reading prompt (Nordwind und Sonne) gate
    // ------------------------------------------------------------------

    /// <summary>
    /// main-042 AC — when the picker is set to German AND the source is
    /// Microphone, the German reading-prompt block is visible. This is the
    /// parallel of <see cref="IsRainbowPassageVisible"/> from main-041, only
    /// the language polarity is flipped. v1 ships with English + German, so
    /// at any time exactly one prompt is shown in Mic mode (or none, in
    /// System Audio mode).
    /// </summary>
    [Fact]
    public void IsGermanReadingPromptVisible_TrueWhenGermanSelected_InMicMode()
    {
        var vm = NewCloningVm();
        Assert.True(vm.IsMicMode);

        vm.Language = VoiceLanguage.German;

        Assert.True(vm.IsGermanReadingPromptVisible);
    }

    /// <summary>
    /// main-042 AC — the English-Mic case keeps the German prompt collapsed.
    /// English defaults; the user must opt into German for the German block
    /// to appear. Guards the "exactly one prompt visible at a time in Mic
    /// mode" invariant alongside <see cref="IsRainbowPassageVisible"/>.
    /// </summary>
    [Fact]
    public void IsGermanReadingPromptVisible_HiddenWhenEnglishSelected_EvenInMicMode()
    {
        var vm = NewCloningVm();
        Assert.Equal(VoiceLanguage.English, vm.Language);
        Assert.True(vm.IsMicMode);

        Assert.False(vm.IsGermanReadingPromptVisible);
    }

    /// <summary>
    /// main-042 AC — toggling back to English collapses the German block.
    /// Guards against an accidental one-way flag, mirrors the equivalent
    /// test for <see cref="IsRainbowPassageVisible"/>.
    /// </summary>
    [Fact]
    public void IsGermanReadingPromptVisible_CollapsedWhenSwitchedBackToEnglish()
    {
        var vm = NewCloningVm();
        vm.Language = VoiceLanguage.German;
        Assert.True(vm.IsGermanReadingPromptVisible);

        vm.Language = VoiceLanguage.English;
        Assert.False(vm.IsGermanReadingPromptVisible);
    }

    /// <summary>
    /// main-042 AC — System Audio mode keeps the German prompt collapsed
    /// regardless of language. The user is not speaking in System Audio, so
    /// a reading prompt is irrelevant (matches the main-034 Mic-only rule).
    /// </summary>
    [Fact]
    public void IsGermanReadingPromptVisible_CollapsedInSystemAudioMode_RegardlessOfLanguage()
    {
        var vm = NewCloningVm();
        vm.Language = VoiceLanguage.German;
        Assert.True(vm.IsGermanReadingPromptVisible);

        vm.SelectedSource = CloningSource.SystemAudio;
        Assert.False(vm.IsGermanReadingPromptVisible);
    }

    /// <summary>
    /// main-042 AC 2 — both prompts are never visible simultaneously. In Mic
    /// mode the language picker switches exactly one of them on; in System
    /// Audio mode both stay collapsed. Pins the contract the XAML relies on
    /// (two parallel Borders, gated on mutually-exclusive view-model flags).
    /// </summary>
    [Fact]
    public void EnglishAndGermanPrompts_AreNeverBothVisible()
    {
        var vm = NewCloningVm();

        // English + Mic → Rainbow only.
        Assert.True(vm.IsRainbowPassageVisible);
        Assert.False(vm.IsGermanReadingPromptVisible);

        // German + Mic → Nordwind only.
        vm.Language = VoiceLanguage.German;
        Assert.False(vm.IsRainbowPassageVisible);
        Assert.True(vm.IsGermanReadingPromptVisible);

        // German + System Audio → neither.
        vm.SelectedSource = CloningSource.SystemAudio;
        Assert.False(vm.IsRainbowPassageVisible);
        Assert.False(vm.IsGermanReadingPromptVisible);

        // English + System Audio → neither.
        vm.Language = VoiceLanguage.English;
        Assert.False(vm.IsRainbowPassageVisible);
        Assert.False(vm.IsGermanReadingPromptVisible);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static VoiceCloningViewModel NewCloningVm()
    {
        // The capture services are interfaces; the VM subscribes to their
        // events in the ctor but only invokes them via Start/Stop. Stubs are
        // enough for property-level coverage. VoiceLibraryService + the
        // cloning client are real instances pointed at a temp data path so
        // the type-check passes — Save isn't called in these tests.
        var temp = new TempDataPath();
        var library = new VoiceLibraryService(temp.Paths, NullLogger<VoiceLibraryService>.Instance);
        var client = new VoiceCloningClient(sidecar: null!, NullLogger<VoiceCloningClient>.Instance);
        return new VoiceCloningViewModel(
            micCapture: new StubAudioCaptureService(),
            loopbackCapture: new StubHighQualityLoopbackService(),
            voiceLibrary: library,
            cloningClient: client,
            logger: NullLogger<VoiceCloningViewModel>.Instance);
    }

    private sealed class StubAudioCaptureService : IAudioCaptureService
    {
        public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
        public event EventHandler? CaptureStarted;
        public event EventHandler<CaptureStoppedEventArgs>? CaptureStopped;

        public bool IsCapturing => false;
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => Array.Empty<AudioDeviceInfo>();
        public void StartCapture(int deviceIndex = -1) { }
        public void StopCapture() { }
        public void Dispose() { }

        // Suppress CS0067 (event never used) — these are part of the contract
        // even though stubs never raise them.
        private void _suppress()
        {
            AudioDataAvailable?.Invoke(this, null!);
            CaptureStarted?.Invoke(this, EventArgs.Empty);
            CaptureStopped?.Invoke(this, null!);
        }
    }

    private sealed class StubHighQualityLoopbackService : IHighQualityLoopbackService
    {
        public event EventHandler<HighQualityAudioEventArgs>? AudioDataAvailable;
        public event EventHandler? CaptureStarted;
        public event EventHandler<CaptureStoppedEventArgs>? CaptureStopped;

        public bool IsCapturing => false;
        public TimeSpan Duration => TimeSpan.Zero;
        public string? TempWavFilePath => null;
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => Array.Empty<AudioDeviceInfo>();
        public void StartCapture(int deviceIndex = -1) { }
        public void StopCapture() { }
        public void Dispose() { }

        private void _suppress()
        {
            AudioDataAvailable?.Invoke(this, null!);
            CaptureStarted?.Invoke(this, EventArgs.Empty);
            CaptureStopped?.Invoke(this, null!);
        }
    }
}
