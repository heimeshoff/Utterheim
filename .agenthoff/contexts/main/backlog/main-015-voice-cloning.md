---
id: main-015
title: Voice cloning — capture sample, export profile, save to library
status: backlog
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-010, main-014]
blocks: []
tags: [frontend, core, voice-library, audio-capture]
---

## Why

Voice cloning is the **core differentiator** identified in the vision —
"own-your-voices", routing different voices to different Claude sessions for
peripheral session identification. Without it, mockingbird is a local TTS player
with eight fixed built-ins; with it, the entire thesis of the app holds.

WhisperHeim already solves the recording-controls UX (level meter, 5-second
minimum, start/stop, name input, save) and the audio-capture plumbing
(microphone + WASAPI loopback). Per ADR 0006 (copy-and-modify), this task
re-uses those components, adapted to drive `pocket_tts.export_voice` instead
of WhisperHeim's TTS profile flow.

## What

A cloning sub-flow on the Voices page (main-014), with:

- **Source toggle**: Microphone / System audio (WASAPI loopback). Both reuse
  WhisperHeim's capture services (copied per ADR 0006, already present in
  `Services\Hotkey\` style of copy-and-modify).
- **Recording controls** (per styleguide §Reusable component map):
  - Audio level meter (live).
  - Duration display, 5-second minimum + progress bar.
  - Start / Stop buttons.
- **Voice name input** (required, must be unique within `library.json`).
- **Save** button:
  - Calls into the pocket-tts sidecar with the captured PCM (new sidecar
    endpoint TBD — refinement should decide whether to extend the existing
    sidecar or invoke `pocket_tts export-voice` as a one-shot subprocess).
  - Receives a `.safetensors` voice profile, writes it to
    `<dataPath>\voices\<name>.safetensors`.
  - Appends a `library.json` entry: `{ name, engine: "pocket-tts",
    isBuiltIn: false, source: "mic" | "loopback" | "import", createdAt,
    sampleSeconds }`.
  - Voice immediately appears on the Voices list (main-014) and in the Speak
    page voice picker (main-013).
- **Import existing clip** (TBD scope): pick a `.wav`/`.mp3` from disk and run
  the same export-voice path. Stretch goal — refine whether v1 includes this.

## Acceptance criteria

- [ ] User can record ≥5 s of microphone audio, name it, save it, and the new
  voice appears in `GET /voices` and on the Voices page within seconds.
- [ ] User can clone from system audio via WASAPI loopback (e.g., capturing
  10 s of an audiobook playing through the system) and save it the same way.
- [ ] Saving creates `<dataPath>\voices\<name>.safetensors` and a matching
  `library.json` entry.
- [ ] A new cloned voice is immediately usable — `POST /speak` with that
  voice name produces audio in the cloned voice within ~2 s.
- [ ] Recording controls match the styleguide / WhisperHeim spec exactly
  (level meter, 5-second minimum, progress bar).
- [ ] Cloning UI is visually nested in the Voices page (per WhisperHeim §6
  Section B), not a separate page.

## Notes

- **Open question for refinement**: how does the C# host actually call
  `pocket_tts.export_voice`? Three plausible shapes:
  1. Extend `pocket_tts.server` with a `POST /export-voice` endpoint (clean,
     keeps the existing sidecar shape).
  2. Spawn a one-shot `python -m pocket_tts export-voice` per clone (slow:
     pays Python + torch import each time).
  3. Wire a long-lived bidirectional channel for both synthesis and export.
     Probably overkill for v1.
  Default direction: option 1. Confirm via reading `pocket_tts/main.py`
  during refinement.
- Reference: ADR 0002 (sidecar shape), ADR 0006 (WhisperHeim reuse form),
  `kyutai-tts-2026-05-01.md` research report (covers the export-voice API
  surface).
- Audio capture services are not yet copied into mockingbird — they were
  deferred from main-009 because the skeleton didn't need them. This task
  is where they land.
- ADR-worthy if option 1 vs 2 turns out to be a meaningful trade-off
  (latency, sidecar coupling). Surface during refinement.
