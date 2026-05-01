# Context: main

## Purpose

The single bounded context for Mockingbird. Owns everything from voice acquisition through synthesis to delivery: voice profile management, sample capture, speak-request queueing, streaming TTS playback, the Claude Code integration surface, and the tray UI that wraps it all.

This is a personal tool with one user and one primary consumer (Claude Code). The whole app is one coherent subject; see `../../context-map.md` for why the candidate sub-boundaries (synthesis, voice-library, voice-capture, claude-bridge, tray-ui) were rejected as separate BCs.

## Classification

**Mixed — primarily core, with supporting and generic regions inside it.**

- **Core**: the *voice diversity* loop — letting the user clone arbitrary voices from any audio source they have, persist them as `.safetensors` profiles, and route them per Claude session. This is the differentiator. No off-the-shelf product does exactly this for this user's workflow.
- **Supporting**: the speak queue, stop semantics, output device selection, tray UI shell. Real work, but not the differentiator — these exist to make the core loop usable.
- **Generic**: audio capture plumbing (WASAPI loopback, microphone), global hotkeys, settings persistence, tray icon, the synthesis engine itself (pocket-tts is consumed, not built). These are reused from WhisperHeim or vendored from libraries.

The fact that the BC straddles all three classifications is a normal property of small personal tools — there isn't enough domain mass to justify cutting along the classification line.

## Core language

From the vision's seed glossary, plus terms that surfaced during boundary analysis:

| Term | Meaning |
|---|---|
| **Voice profile** | A named, persistent representation of a voice that pocket-tts can reuse instantly. `.safetensors` file plus metadata. The unit of voice identity. |
| **Sample clip** | The 5–20-second audio snippet used to create a voice profile. Source: microphone, WASAPI loopback, or imported file. |
| **Built-in voice** | A voice profile shipped with pocket-tts (alba, marius, javert, jean, fantine, cosette, eponine, azelma). |
| **Cloned voice** | A user-created voice profile. |
| **Speak request** | An incoming call carrying `{text, voice ID}`. The unit of work the BC queues, synthesizes, and plays. |
| **Speak queue** | FIFO of pending speak requests. Head plays; tail is appended. Advances on completion or stop. |
| **Stop signal** | A user action (double-tap LCtrl) that halts current playback. Drain-vs-keep semantics is an open question (see vision). |
| **Loopback capture** | Recording the system output device via WASAPI loopback. The "record what I'm hearing" source for voice cloning. |
| **First-chunk latency** | Time from speak request to first audio sample at the speakers. Target ≤2 s; pocket-tts ~200 ms. |
| **Streaming synthesis** | Producing audio in chunks during generation so playback starts before the full utterance is rendered. |
| **Capture session** | An interactive recording episode that produces (or rejects) a single sample clip. |
| **Engine** | The TTS implementation behind a profile. v1 has one engine (pocket-tts). A profile records its engine so future multi-engine selection is possible. |
| **Speak endpoint** | The localhost-only HTTP/IPC surface Claude Code calls to enqueue speak requests. The published interface of this BC. |

## Key actors

- **The user** (single developer) — clones voices, manages the library, configures settings, hits the stop hotkey, watches the tray status. Episodic interaction.
- **Claude Code sessions** (multiple, parallel) — the primary speak-request producers. They call the speak endpoint and expect audio to come out within ~2 s. They do not know about the queue, the voice library, or the engine; they only know `{text, voice}`.
- **pocket-tts** (external engine, consumed) — the conformist dependency. Mockingbird adapts to its API.

## Relationships

This BC is the only domain BC. External relationships:

- **Upstream conformist** to **pocket-tts**: mockingbird adapts to whatever shape pocket-tts exposes (`TTSModel`, voice states, Mimi encoding, `.safetensors`). Wrapped behind a thin internal engine interface to leave room for a second engine later.
- **Open host (published language)** to **Claude Code**: the speak endpoint is a stable, documented localhost surface that any Claude hook can call. The wire format (`{text, voice}` plus stop / status) is the published language.
- **Library consumer** of **WhisperHeim shared services**: audio capture, hotkeys, settings, startup. Not a BC relationship — these are technical libraries with no domain language.

## UI / styleguide gate

This BC has substantial frontend surface (tray icon + tray window with voice library, capture flow, voice-test playback, settings — modeled on WhisperHeim's design.md). 

**Rule:** every frontend-bearing task in this BC `depends_on` the styleguide task **`main-010`** (`todo/main-010-styleguide.md`). Do not ship UI work before the styleguide is in place and the user has signed off on it. This protects the "feels like a first-party Windows app — Mica backdrop, Fluent controls, Segoe UI Variable, the WhisperHeim aesthetic" success criterion in the vision.

Note: the walking skeleton (`main-009`) is exempt — its UI is a placeholder shell whose purpose is foundation, not presentation. Real frontend feature work waits for `main-010`.

## Notes for the architect

The boundary analysis already surfaced four architectural decisions the architect step needs to make — they are *not* context-map decisions:

1. Python-vs-C# integration shape for pocket-tts (sidecar process / bundled runtime / sherpa-onnx ONNX). All three keep the BC structure intact.
2. Claude-to-mockingbird transport (HTTP / named pipe / CLI). All three are valid implementations of the open-host speak endpoint.
3. Stop-signal semantics (drain queue vs only stop current). Internal to the BC.
4. WhisperHeim reuse form (shared library / copy-and-modify / submodule). Outside the BC frame — pick what's least painful for two-app maintenance.
