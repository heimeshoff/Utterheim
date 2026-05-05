// Rainbow Passage reading prompt for microphone voice cloning (main-034).
//
// Source: Grant Fairbanks, "Voice and Articulation Drillbook", 2nd ed., New
// York: Harper & Row, 1960 — public domain. The text below is the canonical
// opening two sentences of the Rainbow Passage as reproduced verbatim across
// the speech-pathology literature.
//
// Originally we intended to cite a University of York linguistics / clinical
// speech-science page that hosts the passage; at refinement time the page was
// blocked by content filtering, and at worker execute time none of the
// candidate york.ac.uk URLs (language/ppt/rainbowpassage.html, the
// languageandlinguisticscience media path, depts/lang/clinical/rainbow.html)
// resolved — they all 404. The York attribution in the on-screen caption is
// retained per the user's explicit request (main-034 visual spec).
//
// Length: ~10–15 s of conversational reading. Comfortably above the 5-s
// minimum and below the 30-s soft cap that VoiceCloningViewModel enforces.

namespace Mockingbird.ViewModels.Pages;

/// <summary>
/// Reading-prompt text shown above the audio level meter when the user is
/// cloning a voice in Microphone mode. Static, English-only, no localisation
/// in v1 (see main-034 deferred-scope notes).
/// </summary>
public static class RainbowPassage
{
    /// <summary>
    /// First two sentences of the Rainbow Passage (Fairbanks 1960). Verbatim
    /// — no curly quotes, no reflow, straight ASCII apostrophes only.
    /// </summary>
    public const string OpeningTwoSentences =
        "The rainbow is a division of white light into many beautiful colors. " +
        "These take the shape of a long round arch, with its path high above, " +
        "and its two ends apparently beyond the horizon.";
}
