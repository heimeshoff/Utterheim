---
id: main-042
title: German reading prompt for the clone-a-new-voice flow
status: done
type: feature
context: main
created: 2026-05-18
completed: 2026-05-18
commit: e96764e
depends_on: [main-041]
blocks: []
tags: [multilingual, ui, voices-page, copy]
related_adrs: []
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-034]
---

## Why
`main-034` added the Rainbow Passage as a reading prompt in the
clone-a-new-voice mic flow, so users have a phonetically-balanced English
passage to read aloud. With German now a first-class language (after
`main-035` / `main-040` / `main-041`), the German clone path needs an
equivalent — a short German passage chosen for diction breadth, similar in
purpose to the Rainbow Passage's role for English.

## What
Add a German reading-prompt block on the Voices page that shows when the
clone-flow language picker is set to **German** (parallel to the existing
English Rainbow Passage block).

### Passage (pinned)
The **first two sentences of *Nordwind und Sonne*** (Aesop's fable; standard
phonetic test passage used in IPA / dialect work; public domain).
Matches the first-two-sentences pattern `main-034` used for the Rainbow
Passage. Worker fetches the canonical text — the conventional opening is:

> *Einst stritten sich Nordwind und Sonne, wer von ihnen beiden wohl der
> Stärkere wäre, als ein Wanderer, der in einen warmen Mantel gehüllt war,
> des Weges daherkam. Sie wurden einig, dass derjenige für den Stärkeren
> gelten sollte, der den Wanderer zwingen würde, seinen Mantel abzunehmen.*

If a source-comment URL is wanted (matching `main-034`'s pattern), the
Wikipedia "Nordwind und Sonne" article is the standard citation. Fallback
attribution if the URL is unreachable at build time: "Aesop / traditional,
public domain".

### UI (pinned)
- Block layout mirrors the English block from `main-034`: caption + passage
  text + attribution.
- Shows only when language picker = German AND source = Microphone (same
  Mic-only rule the English block uses).
- **Caption: *"Lies bitte vor:"*** (informal/du). This sets the German tone
  for the tray app going forward — all future German UI copy uses du, not
  Sie.
- **Attribution caption: *"Nordwind und Sonne (Aesop, gemeinfrei)"***.

## Acceptance criteria
- [ ] When the clone-flow language picker is set to German (from `main-041`),
      the German reading-prompt block is visible (Mic mode only).
- [ ] The English Rainbow Passage block does NOT show simultaneously.
- [ ] A constants class (parallel to `RainbowPassage`, e.g.
      `NordwindUndSonne`) holds the German passage text and attribution
      strings.
- [ ] Caption renders exactly *"Lies bitte vor:"*; attribution renders
      exactly *"Nordwind und Sonne (Aesop, gemeinfrei)"*.
- [ ] Passage is the first two sentences of Nordwind und Sonne (text above
      is the canonical reference).
- [ ] Source-URL comment in the constants class (best-effort, like
      `main-034`); Wikipedia article URL acceptable.

## Notes
The tone choice (du, not Sie) is deliberate and load-bearing for future
German UI — match it in any later German copy added to the app.

Small code surface — the heavy structural lift is `main-041`. If the worker
is already in the Voices page for `main-041` when picking this up, bundle.

## Outcome

The Voices page **Clone a new voice** card now renders a German reading
prompt block (Nordwind und Sonne) when the language picker is set to German
and the source is Microphone. It sits directly below the existing English
Rainbow Passage block; the two are mutually exclusive — exactly one is
visible in Mic mode, neither in System Audio mode.

The German block mirrors the English one's visual treatment
(`SubtleFillColorSecondaryBrush`, 1-pt `ControlStrokeColorDefault`, 4-pt
corner, 12-pt padding, bottom margin) and structure
(caption + passage + attribution). Caption renders "Lies bitte vor:" in
informal du-form — this is a deliberate tone-setter for all later German
UI copy per the task spec. Attribution renders
"Nordwind und Sonne (Aesop, gemeinfrei)". The passage is the canonical
first two sentences of Nordwind und Sonne, embedded verbatim from the
task file's pinned text.

Text, caption, and attribution live on a new static class
`Utterheim.ViewModels.Pages.NordwindUndSonne` (`OpeningTwoSentences`,
`Caption`, `Attribution` `const string`s), parallel to the
`RainbowPassage` class from main-034. The XAML binds them via
`{x:Static}` — no view-model passage property, no localisation framework
in v1. Source comment references the Wikipedia "Nordwind und Sonne"
article URL.

Visibility is gated by a new view-model flag
`VoiceCloningViewModel.IsGermanReadingPromptVisible`
(`IsMicMode && Language == German`), parallel to the existing
`IsRainbowPassageVisible` flag. Both are recomputed on `SelectedSource`
and `Language` changes via `[NotifyPropertyChangedFor]`. Five new tests
in `VoiceCloningViewModelLanguageTests` exercise the full truth table
(German-Mic visible, English-Mic hidden, toggle-back collapses, System
Audio collapses both, the mutual-exclusion invariant).

`dotnet build utterheim.sln -c Debug` → 0 warnings, 0 errors.
`dotnet test utterheim.sln` → 26/26 passing.

No ADR was warranted — the implementation choices (constants class vs.
VM property; informal du-form caption; Wikipedia citation) were either
explicitly pinned in the task spec or already established by main-034's
parallel pattern.

### Key files

- `src\Utterheim\ViewModels\Pages\NordwindUndSonne.cs` (new) — German
  passage + caption + attribution constants, parallel to `RainbowPassage`.
- `src\Utterheim\ViewModels\Pages\VoiceCloningViewModel.cs` — new
  `IsGermanReadingPromptVisible` flag, wired into the
  `[NotifyPropertyChangedFor]` chains on `SelectedSource` and `Language`.
- `src\Utterheim\Views\Pages\VoicesPage.xaml` — new German prompt `Border`
  inserted directly below the existing Rainbow Passage block; comment on
  the English block updated to mention the German counterpart.
- `src\Utterheim.Tests\Voices\VoiceCloningViewModelLanguageTests.cs` —
  five new tests covering the German-prompt truth table and the
  mutual-exclusion invariant between the two prompts.
- `.agentheim\contexts\main\README.md` — file map gains
  `NordwindUndSonne.cs`; the Rainbow Passage prompt section is rewritten
  as a unified "Reading-prompt blocks" section describing both languages
  and their gating flags.
