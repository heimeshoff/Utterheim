---
id: main-010
title: Styleguide — adapt WhisperHeim's design language and the speaking-person logo
status: todo
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-009]
blocks: []
tags: [foundation, design-system, gate]
---

## Why

The user explicitly asked that mockingbird use **the same UI technology and design as WhisperHeim, but with a speaking-person logo instead of a microphone**. This task captures and adapts WhisperHeim's design language so that every subsequent frontend task in mockingbird has a single source of truth.

This is **the styleguide gate**: every frontend-bearing task captured for mockingbird must `depends_on` this task, and this task must be **reviewed and signed off by the user** before any other frontend feature is promoted to `todo/`. See `contexts/main/README.md` for the gate rule.

The walking skeleton (main-009) is allowed to ship a placeholder UI shell because its purpose is foundation, not user-facing presentation. Main-010 is where presentation begins.

## What

Produce a small, concrete styleguide artefact in the project that:

1. **References, doesn't duplicate, WhisperHeim's design.md.** The relationship is "we inherit, with these specific changes." The styleguide should *point* at `C:\src\heimeshoff\tooling\WhisperHeim\design.md` for the unchanged parts (Mica backdrop, Fluent controls, Segoe UI Variable, sidebar nav layout, page composition rules, color/contrast principles, recording-controls component spec from section 6).

2. **Documents the divergences.** What does mockingbird do differently from WhisperHeim? At minimum:
   - **Logo / brand mark**: speaking-person silhouette (vs WhisperHeim's microphone). Provide the icon in PNG and ICO formats sized for tray, taskbar, and About page.
   - **Page set**: mockingbird doesn't have Dictation, Templates, Transcribe Files, or Transcripts pages. It will have (in eventual feature-task order): Speak (main page, equivalent to WhisperHeim's TTS section A — Speak), Voices (equivalent to WhisperHeim's TTS section B — Voices), Settings, About.
   - **Tagline**: replace WhisperHeim's "Live dictation powered by Whisper" with something appropriate (e.g., "Local voices for Claude Code" — final wording is part of the styleguide review).
   - **Hotkey reference**: mockingbird's stop hotkey is **double-tap LCtrl**. WhisperHeim's TTS hotkey was `Ctrl+Win+Ä` for "read selected text aloud" — mockingbird does not implement that gesture (Claude Code drives speak requests via HTTP, not text-selection).
   - **Tray menu items**: "Show window", "Stop speaking", "Exit" (per main-009). Different from WhisperHeim's call-recording-focused menu.

3. **Lists the reusable components mockingbird inherits unchanged.** From WhisperHeim's design.md section 6 (TTS), the following components apply directly:
   - The voice-cloning recording controls (level meter, duration display with 5-second minimum + progress bar, start/stop buttons, voice name input, save button).
   - The microphone vs system-audio source toggle.
   - The Speak section composition (text input → voice selector → play/stop/save buttons → status line).
   - The voices list with preview / delete affordances.

4. **Picks the speaking-person logo.** Source the icon (commission, find a permissively-licensed asset, or generate one matching WhisperHeim's iconography). Place it under `assets/branding/` in the mockingbird repo. Include sizes 16, 24, 32, 48, 64, 128, 256, 512 and an .ico for Windows tray.

5. **Reviews divergences with the user.** Sign-off is a hard gate.

## Acceptance criteria

- [ ] `docs/styleguide.md` (or equivalent) exists in the mockingbird repo, structured as: "Inherited from WhisperHeim" (link + brief summary) + "Mockingbird divergences" (the specific list above) + "Reusable component map" + "Brand assets" (paths to logo files).
- [ ] Speaking-person logo exists in `assets/branding/` in the required sizes + `.ico` and is shown:
  - In the WPF window's title bar / icon resource.
  - In the system tray (replaces the placeholder from main-009).
  - On the About page.
- [ ] The placeholder shell from main-009 is updated to use the final logo and tagline.
- [ ] **User has reviewed the styleguide and signed off in writing** (via PR comment, commit message acknowledgement, or a `signed-off-by:` line in the styleguide doc itself). No frontend feature task is promoted to `todo/` before this checkbox is ticked.
- [ ] `contexts/main/README.md`'s "UI / styleguide gate" section is updated to reference this task by id (`main-010`) and to point at the resulting `docs/styleguide.md` once it exists.

## Notes

- The strategic-modeler decided mockingbird is a single-BC project (see `.agenthoff/context-map.md`); the brainstorm protocol's default of creating a separate `contexts/design-system/` BC is overruled in favour of one styleguide task inside `contexts/main/`. The gate behaviour (every frontend-bearing task `depends_on` this one) still applies.
- The user will likely want to validate the speaking-person logo visually before sign-off. Plan for an iteration loop on the logo artwork, not a single-shot delivery.
- Don't attempt to upgrade WhisperHeim's design.md from this task. If divergences accumulate that *should* go upstream (e.g., a better recording-controls component), capture them as a separate refinement task — not part of this one.
- After sign-off, the next round of frontend tasks (Speak page, Voices page, Settings, About) can be modelled and queued. They each carry `depends_on: [main-010]`.
