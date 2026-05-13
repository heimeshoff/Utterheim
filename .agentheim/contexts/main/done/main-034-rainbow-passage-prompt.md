---
id: main-034
title: Rainbow Passage prompt for microphone voice cloning
status: done
type: feature
context: main
created: 2026-05-05
completed: 2026-05-05
commit: 4ebd671
depends_on: [main-010, main-025]
blocks: []
tags: [frontend, voice-cloning, voices-page, ux, microphone]
---

## Why

When a user clicks **Start** in Microphone mode on the Voices page's
**Clone a new voice** panel (main-025), they have to spontaneously
generate 5–30 seconds of speech. Cold-starting your own voice is
awkward — people stammer, restart, or trail off, which yields a
poor sample for pocket-tts to encode.

Speech-research labs solve this with a **standard reading passage**:
phonetically balanced text the speaker reads aloud. The output is
predictably paced, covers a wide phoneme distribution, and removes
the "what do I say?" friction. The **Rainbow Passage** (Fairbanks 1960,
*Voice and Articulation Drillbook* — public domain, hosted on
York.ac.uk linguistics / speech-pathology resources) is the canonical
example. Its first two sentences already exceed the 5-second minimum
the cloning flow enforces.

Outcome: better cloned voices on the first try, less abandoned
recordings, less embarrassment.

## What

Show the **first two sentences of the Rainbow Passage** inside the
cloning panel **only when Microphone mode is selected** (confirmed
2026-05-05). System Audio mode never shows the block — the user
isn't speaking in that mode, so a reading prompt is irrelevant.

### Exact text to embed

```
The rainbow is a division of white light into many beautiful colors. These take the shape of a long round arch, with its path high above, and its two ends apparently beyond the horizon.
```

This is the canonical opening of the Fairbanks 1960 passage. ~10–15 s
of conversational reading time, comfortably under the 30-s soft cap
in main-025 and well above the 5-s minimum. No `ScrollViewer` needed —
two sentences fit a single wrapped block.

### Placement

In `src\Utterheim\Views\Pages\VoicesPage.xaml`, just below the
Mic-mode `TipText` `TextBlock` (currently around line 116) and above
the audio level meter Grid (around line 122). Visibility bound to
`ViewModel.Cloning.IsMicMode` (same converter the mic device selector
uses at line 105) so System Audio mode stays unaffected.

### Visual

- A bordered / subtly tinted block — `Border` with thin
  `ControlStrokeColorDefaultBrush`, `CornerRadius="4"`,
  `Background="{DynamicResource SubtleFillColorSecondaryBrush}"`.
  Padding `12`. Margin `0,0,0,12`.
- Inside: a small `SemiBold` heading "Read this aloud:" followed by
  the passage text in a `TextBlock` with `TextWrapping="Wrap"`,
  `LineHeight="20"`, `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`.
- Below the text, a dim caption row: "The Rainbow Passage —
  University of York", `FontSize="11"`,
  `Foreground="{DynamicResource TextFillColorSecondaryBrush}"`.

The block is **read-only display**; nothing about it changes during
recording in v1 (no auto-scroll, no current-sentence highlight —
explicitly out of scope).

### Source / attribution

Two sentences shipped as a static `const string` (or `<x:String>`
resource) — no runtime fetch, no localisation in v1. Embedded in
`VoiceCloningViewModel` (or a small `RainbowPassage.cs` constants
file alongside it).

The worker should locate the exact York-hosted page hosting this
passage and record the URL in a comment header on whichever file
holds the constant. Fallback if the York page is unreachable: cite
"Fairbanks 1960, *Voice and Articulation Drillbook* (public domain)"
in the comment, and still display the "University of York" caption
in the UI per the visual spec above (the user explicitly asked for
the York attribution).

## Acceptance criteria

- [ ] In Microphone mode, the Voices page **Clone a new voice**
  panel renders a passage block between the mic tip and the level
  meter.
- [ ] In System Audio mode, the passage block is **not visible**
  (collapsed via `IsMicMode` binding, same pattern as the existing
  device selectors).
- [ ] The passage text content is exactly the two sentences quoted
  above — no edits, no reflow, no curly-quote substitution.
- [ ] An attribution caption "The Rainbow Passage — University of
  York" appears beneath the text in the secondary-text style.
- [ ] A code comment at the constant's declaration site cites the
  source URL on york.ac.uk (or, if the page is unreachable at
  worker execute time, "Fairbanks 1960, *Voice and Articulation
  Drillbook* (public domain)").
- [ ] The block is keyboard-accessible only insofar as it doesn't
  trap focus — it's display text, no interactive elements.
- [ ] Visual matches styleguide (main-010) — Mica-friendly subtle
  fill, Segoe UI Variable, no bespoke colours.
- [ ] Build clean: `dotnet build utterheim.sln -c Debug` produces
  0 errors, 0 warnings.

## Notes

### Refinement decisions (resolved 2026-05-05)

- **Mic only**: confirmed. System Audio mode keeps the block
  collapsed via `IsMicMode`.
- **Length**: first two sentences (~10–15 s reading), not the full
  passage and not just paragraph one. Matches the user's stated
  "5–10 seconds of clear voice" goal, no `ScrollViewer` needed.
- **Source URL**: deferred to worker — research blocked by content
  filter at refinement time. Worker captures the canonical
  york.ac.uk URL in a code comment, falling back to public-domain
  Fairbanks 1960 attribution if York is unreachable. Either way the
  UI caption stays "The Rainbow Passage — University of York".

### Deferred (out of scope here)

- Live "current sentence" highlight or auto-scroll.
- Audio playback of a reference reading.
- Multi-language passages.
- User-customisable passage / "paste your own text".
- Coaching feedback ("you skipped a sentence", "you're too quiet").
- Localisation — v1 is English-only. The cloning flow itself is
  language-agnostic (pocket-tts handles whatever the user speaks),
  but the prompt isn't. Note for a future task.

### Files likely to change

- `src\Utterheim\Views\Pages\VoicesPage.xaml` — add the passage
  `Border` block under the mic tip.
- `src\Utterheim\ViewModels\Pages\VoiceCloningViewModel.cs` — if
  exposing the text via property; otherwise a sibling
  `RainbowPassage.cs` constants file.

## Outcome

Voices page now renders a "Read this aloud:" prompt block in the
**Clone a new voice** card whenever Microphone mode is selected. The
block contains the canonical first two sentences of the Rainbow Passage
followed by the requested "The Rainbow Passage — University of York"
caption, styled with `SubtleFillColorSecondaryBrush`,
`ControlStrokeColorDefaultBrush`, 4-pt corner, 12-pt padding, and bottom
margin matching the rest of the panel. System Audio mode keeps the
block collapsed via the existing `Cloning.IsMicMode` flag (same binding
pattern as the mic device selector).

The passage text lives in a new static class
`Utterheim.ViewModels.Pages.RainbowPassage` (`OpeningTwoSentences`
const), bound via `{x:Static vm:RainbowPassage.OpeningTwoSentences}` —
no view-model property, no localisation in v1, no runtime fetch. The
file's comment header documents the source: at execute time none of
the candidate `york.ac.uk` hosts (`language/ppt/rainbowpassage.html`,
`media/languageandlinguisticscience/...`,
`depts/lang/clinical/rainbow.html`) resolved, so the canonical citation
in code falls back to *Fairbanks 1960, Voice and Articulation Drillbook
(public domain)* per the task's explicit fallback instruction. The
on-screen caption stays "The Rainbow Passage — University of York"
per the visual spec.

`dotnet build utterheim.sln -c Debug` → **0 warnings, 0 errors**.

### Key files

- `src\Utterheim\Views\Pages\VoicesPage.xaml` — new `Border` block
  inserted between the mic tip and the audio level meter.
- `src\Utterheim\ViewModels\Pages\RainbowPassage.cs` (new) — static
  class with the canonical text and source-attribution comment header.
- `.agentheim\contexts\main\README.md` — Cloning panel section gains a
  Rainbow Passage prompt bullet; file map gains the new constants file.

No ADR was warranted — both implementation choices the task left open
(constants file vs. VM property; Fairbanks fallback) were already
explicitly listed as acceptable in the task spec, and the fallback
trail is fully recoverable from this task file plus the
`RainbowPassage.cs` comment header.
