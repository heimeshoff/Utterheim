// German reading prompt for microphone voice cloning (main-042).
//
// Source: "Nordwind und Sonne" — German prose adaptation of Aesop's fable
// "The North Wind and the Sun" (Aesop ca. 600 BCE; the German wording used
// here is the conventional opening reproduced verbatim across phonetics /
// IPA reference works). Public domain ("gemeinfrei") — Aesop's original is
// long out of copyright and the standard German wording carries no
// modern-author claim.
//
// This passage is the canonical German equivalent of the Rainbow Passage
// for English (main-034): the International Phonetic Association and most
// dialect-research labs use the first two sentences of Nordwind und Sonne
// as a phonetically broad reading sample. Its first two sentences exceed
// the 5-second minimum the cloning flow enforces.
//
// Reference URL (Wikipedia article on the passage; canonical citation):
//   https://de.wikipedia.org/wiki/Nordwind_und_Sonne
// At worker execute time the URL was not fetched — the wording below is the
// conventional opening as the task file pinned it. Fallback attribution if
// the URL is unreachable: "Aesop / traditional, gemeinfrei".
//
// Tone: the on-screen caption uses informal du ("Lies bitte vor:"), per
// main-042 — this sets the German tone for the tray app going forward; all
// later German UI copy uses du, not Sie.
//
// Length: ~10–15 s of conversational reading at a moderate pace.
// Comfortably above the 5-s minimum and below the 30-s soft cap that
// VoiceCloningViewModel enforces.

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// Reading-prompt text shown above the audio level meter when the user is
/// cloning a voice in Microphone mode and the language picker is set to
/// German. Static, German-only — the English counterpart lives in
/// <see cref="RainbowPassage"/>. No localisation logic in v1; the language
/// picker (main-041) selects which constants class the XAML binds to via
/// two mutually-exclusive visibility flags on
/// <see cref="VoiceCloningViewModel"/>.
/// </summary>
public static class NordwindUndSonne
{
    /// <summary>
    /// First two sentences of Nordwind und Sonne (Aesop, gemeinfrei).
    /// Verbatim — straight ASCII apostrophes only, no curly quotes, no
    /// reflow. Matches the pinned text in main-042's task description.
    /// </summary>
    public const string OpeningTwoSentences =
        "Einst stritten sich Nordwind und Sonne, wer von ihnen beiden wohl " +
        "der Stärkere wäre, als ein Wanderer, der in einen warmen Mantel " +
        "gehüllt war, des Weges daherkam. Sie wurden einig, dass derjenige " +
        "für den Stärkeren gelten sollte, der den Wanderer zwingen würde, " +
        "seinen Mantel abzunehmen.";

    /// <summary>
    /// Heading shown above the passage. Informal du-form per main-042's
    /// tone decision.
    /// </summary>
    public const string Caption = "Lies bitte vor:";

    /// <summary>
    /// Attribution line shown beneath the passage, parallel to the
    /// "The Rainbow Passage — University of York" line on the English
    /// block.
    /// </summary>
    public const string Attribution = "Nordwind und Sonne (Aesop, gemeinfrei)";
}
