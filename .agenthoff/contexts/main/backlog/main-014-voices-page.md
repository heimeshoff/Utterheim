---
id: main-014
title: Voices page — voice library list with preview and delete
status: backlog
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-010, main-011, main-020]
blocks: []
tags: [frontend, page, voice-library]
---

## Why

Once voice cloning lands (main-015) the user will accumulate voice profiles and
needs a place to browse, audition, and prune them. Even before cloning, the
page makes the eight pocket-tts built-ins discoverable — the user can hear
"alba" before assigning it to a Claude session. Mirrors WhisperHeim's TTS
section B (Voices), specifically the "Custom Voices list" component plus the
built-in voices view.

## What

The Voices page in the WPF shell, with:

- A list of all voice profiles surfaced by `GET /voices`, grouped or labelled
  by `isBuiltIn` (built-ins first, cloned voices second — TBD in refinement).
- Per-row affordances:
  - **Preview** — synthesises a short canned phrase ("Hello, this is
    {voiceName}.") through the voice and plays it locally. Reuses the same
    in-process synthesis path the Speak page uses.
  - **Delete** — only enabled for cloned voices. Confirms, then removes the
    profile from `library.json` and deletes the underlying `.safetensors`
    file. Built-ins are read-only.
- Per-row metadata: name, engine, isBuiltIn flag, created-at (cloned only),
  source (mic / loopback / import) for cloned voices.

Out of scope: the cloning flow itself — that lives on the same page conceptually
(per WhisperHeim §6 Section B) but is captured separately as **main-015** so the
two can ship independently if needed. This task ships the list + preview + delete
shell with cloning controls absent.

## Acceptance criteria

- [ ] Voices page is reachable from the sidebar nav.
- [ ] Eight pocket-tts built-ins (alba, marius, javert, jean, fantine, cosette,
  eponine, azelma) appear in the list with `isBuiltIn` true.
- [ ] Preview button on each row plays a canned phrase in that voice within
  ~2 s, using the same in-process synthesis path as the Speak page.
- [ ] Delete is hidden / disabled for built-ins.
- [ ] Delete on a cloned voice removes the row, the `.safetensors` file, and
  the entry in `library.json`.
- [ ] Visual matches the styleguide.

## Notes

- Reference: `docs/styleguide.md` §Reusable component map → "Voices list with
  preview + delete affordances", WhisperHeim `design.md` §6 Section B.
- v1 storage layout: `<dataPath>\voices\library.json` + per-voice
  `.safetensors`. ADR 0005 governs paths.
- Open question for refinement: search / filter / tags? Vision defers until the
  library reaches ~15 voices. Don't build it now.
- Open question for refinement: voices-per-Claude-session routing UI lives where?
  Settings? A separate Sessions page? Out of scope here, but worth flagging
  during refinement so this page doesn't accidentally absorb that surface.
