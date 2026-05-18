---
id: main-041
title: Voices page — language picker in clone flow + per-voice language column
status: todo
type: feature
context: main
created: 2026-05-18
completed:
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
