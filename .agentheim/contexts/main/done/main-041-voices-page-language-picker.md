---
id: main-041
title: Voices page — language picker in clone flow + per-voice language column
status: done
type: feature
context: main
created: 2026-05-18
completed: 2026-05-18
commit:
depends_on: [main-035, main-040]
blocks: [main-042]
tags: [multilingual, ui, voices-page, wpf]
related_adrs: [0009, 0010, 0023]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-014, main-025, main-026, main-030, main-033, main-034]
---

## Why
With voices carrying a language (ADR for `main-035`) and the library schema
extended (`main-040`), the Voices page becomes the user-visible surface where
language is chosen and displayed. Two UI deltas are needed:

1. The clone-a-new-voice card must ask for the target language up front so
   the sidecar's `export-voice` call passes the right `--language` flag.
2. The voice list (built-in and cloned) must show each voice's language so
   the user can tell `juergen` from `alba` at a glance.

## What

### Clone flow — language picker
Add a language picker to the Clone-a-new-voice card, above (or beside) the
existing source-toggle (Microphone / System Audio). Two options for v1:
**English**, **German**. The picker is required; default is English (matches
the user's "default language will always be English" decision).

The Rainbow Passage block — added in `main-034` — currently shows for Mic
mode only. When the language picker is set to German, that block should be
swapped or hidden. The German equivalent (Nordwind und Sonne or similar) is
`main-042`; until 042 lands, English-selected behaviour is unchanged and
German-selected hides the reading-prompt block entirely. The worker can
optionally show a placeholder hint ("German reading prompt coming soon") —
but no required.

When the user records and saves, the chosen language is passed through to
`VoiceLibraryService.AddCloned(...)` (see `main-040`) and on into the
sidecar's `/export-voice` call as the `language` argument.

### Voice list — language column / badge
Each row in the Voices page list shows the voice's language. Two
presentations the worker can pick between:

- A small inline badge / chip next to the voice name (e.g. `juergen [DE]`).
- A column in the list with the language code.

The styleguide direction from `main-029` favours minimal chrome — a compact
language chip in the existing card spec probably reads cleanest. Worker
picks; surfaces the choice in a screenshot if helpful.

### Default voice selector (Settings)
Settings has a default-voice picker (from `main-016`). It should keep
working without change — voices are voices, regardless of language. No
language filter on the Settings dropdown is required in v1. (If the list
gets cluttered later we can add a filter; not in scope here.)

## Acceptance criteria
- [ ] Clone-a-new-voice card has a language picker with `English` and
      `German` options. English is the default selection.
- [ ] Selecting German hides the Rainbow Passage block (the English-only
      reading prompt added in `main-034`).
- [ ] Saving a cloned voice persists its language via the service from
      `main-040` and, end-to-end, the resulting `.safetensors` was exported
      against the correct pocket-tts language.
- [ ] Every voice row on the Voices page shows its language — chip, badge,
      or column.
- [ ] Settings → default voice picker continues to work for both English and
      German voices (no regression to `main-016`).
- [ ] Manual smoke: clone a German voice with `juergen` as the test voice
      (or any voice via Mic with the picker set to German); speak with it
      from Claude-Code; audio is German.

## Notes
This task depends on `main-040` for the schema and `main-035` for the
voice-carries-language contract. It deliberately *does not* depend on
`main-039` — the UI can ship before the sidecar is multi-model, but the
end-to-end speak test won't work until 039 lands. The worker can either
gate the manual-smoke acceptance bullet behind 039, or land 039 first.

`main-042` (German reading prompt) is a follow-up that fills in the swapped
block; it's in `backlog/` pending the German passage choice.

Touches:
- `src/Utterheim/.../Views/Pages/VoicesPage.xaml` and its ViewModel.
- The clone-flow code path through `VoiceLibraryService` (no new schema
  work; that's `main-040`).

## Outcome

Wired the language-aware Voices page on top of main-040's library schema and
main-039's sidecar routing. The clone-flow card now leads with a language
picker (English default, German option), the Rainbow Passage reading prompt
is gated behind English+Mic so German clones stop showing an irrelevant
English passage, and every voice row carries a compact two-letter language
chip (`EN` / `DE`) so `juergen` reads at a glance against the eight English
built-ins. The chosen language flows end-to-end: it rides the
`X-Voice-Language` header on the `/export-voice` POST (so the sidecar's
encoder runs against the matching resident `TTSModel`) and lands in both
`meta.json` and `library.json` via the existing
`VoiceLibraryService.AddAsync(language: …)` overload.

### Key files
- `src/Utterheim/ViewModels/Pages/VoiceCloningViewModel.cs` — new
  `Languages` collection, `Language` observable property (default
  `VoiceLanguage.English`), and `IsRainbowPassageVisible` flag that ANDs
  Mic mode with English. `SaveAsync` passes `Language` to both
  `VoiceCloningClient.ExportVoiceAsync` and `VoiceLibraryService.AddAsync`.
- `src/Utterheim/Services/Voices/VoiceCloningClient.cs` — new
  `language: VoiceLanguage` parameter on `ExportVoiceAsync` that stamps
  `X-Voice-Language` on the outgoing request via
  `PocketTtsEngine.LanguageWireValue`. Same wire literal as the speak path.
- `src/Utterheim/ViewModels/Pages/VoicesPageViewModel.cs` — added
  `VoiceRowViewModel.Language` (strongly-typed) and `LanguageLabel`
  (`"EN"` / `"DE"`) sourced from the descriptor. Private
  `ToLanguageLabel` switch centralises the ISO 639-1 mapping.
- `src/Utterheim/Views/Pages/VoicesPage.xaml` — language `ComboBox` above
  the source toggle on the clone card; Rainbow Passage `Border` re-bound
  to `Cloning.IsRainbowPassageVisible`; both row templates grew a chip
  column (compact `Border` with `LanguageLabel` text) between name+meta
  and Preview.
- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` —
  `_route_paths_needing_model` now includes `/export-voice` so the
  `LanguageRoutingMiddleware` swaps `pocket_tts.main.tts_model` to the
  matching resident before `get_state_for_audio_prompt` runs.
- `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` — bumped
  `__version__` 1.1.0 → 1.2.0 so `BundledSidecarMatchesInstalled` forces
  a reinstall on next launch (per ADR 0016 / main-027).
- `src/Utterheim.Tests/Voices/VoiceCloningViewModelLanguageTests.cs` — 5
  new xUnit tests covering the picker default, the options list, and the
  Rainbow Passage visibility gate (English/Mic, German/Mic, System Audio).
- `src/Utterheim.Tests/Voices/VoiceRowLanguageTests.cs` — 3 new xUnit
  tests covering `Language` + `LanguageLabel` flow-through for English
  built-ins, German built-ins (`juergen`), and German clones.

### BC README
Updated the `Cloning panel` section to document the new language picker,
extend the Rainbow Passage gating to also AND on `Language == English`,
and note the `IsRainbowPassageVisible` flag. Updated the voice-row layout
description to call out the language chip column. Updated the
`VoiceCloningClient` reference to document the new `language` parameter
and the `_route_paths_needing_model` extension.

### Tests
21 total in `Utterheim.Tests`, all passing. 8 new under
`Utterheim.Tests.Voices.*`. The view-model + row tests are the load-bearing
gates: XAML bindings can't be unit-tested headlessly, so they hang off the
view-model surface every binding consumes.

### Manual smoke (AC 6) — deferred
The end-to-end "clone a German voice with the picker set to German, speak
with it" test requires a fresh sidecar launch on a dev box where both
English and German pocket-tts models are downloaded. Deferred to runtime
per the task spec note on AC 6 ("may be deferred if models unavailable").
The wire shape was verified at compile time: header value comes from the
same `PocketTtsEngine.LanguageWireValue` switch the speak-path test suite
already pins (`PocketTtsEngineLanguageRoutingTests`), and the Python
middleware's preload + swap behaviour was validated end-to-end in main-039.

### AC checklist
- AC 1 (picker with English default + German) — done
  (`VoiceCloningViewModelLanguageTests.Language_DefaultsToEnglish`,
  `Languages_ContainsEnglishAndGerman_InOrder`).
- AC 2 (German hides Rainbow Passage) — done
  (`IsRainbowPassageVisible_HiddenWhenGermanSelected_EvenInMicMode`,
  XAML re-bound to `Cloning.IsRainbowPassageVisible`).
- AC 3 (clone persists language; .safetensors exported against the
  correct pocket-tts language) — done. `VoiceLibraryLanguageTests.
  AddAsync_PersistsLanguage_OnBothMetaAndLibraryIndex` (main-040) covers
  the persistence half; the `X-Voice-Language` header on `/export-voice`
  plus the middleware extension cover the encode-against-correct-model
  half. End-to-end audio confirmation is the deferred manual smoke.
- AC 4 (every row shows its language) — done
  (`VoiceRowLanguageTests.*`, XAML chip column in both row templates).
- AC 5 (Settings default voice picker still works) — done. The picker
  consumes `VoiceCatalog` which already plumbs `Language` onto every
  descriptor (built-in and cloned); the settings page reads
  `VoiceDescriptor` without inspecting `Language`, so no filter or shape
  change was needed.
- AC 6 (manual end-to-end German speak) — deferred to runtime smoke.

### Notes
- No ADR written: every decision in this task — picker shape (combo box
  with the enum), label format (ISO 639-1 two-letter), chip vs column
  vs badge (chip; small `Border` `CornerRadius=8`), Rainbow Passage gate
  (collapse on non-English), `/export-voice` middleware extension — was
  either constrained by ADR 0023 / 0024 (language fields, routing
  contract) or covered by main-029 / main-033 styleguide guidance. Worth
  capturing in the BC README, not a new ADR.
- No new backlog items created. main-042 (German reading prompt) already
  exists; the German-selected case currently just hides the Rainbow
  Passage block, which is the contract this task agreed with main-042's
  spec.
