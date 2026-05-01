# Vision: Mockingbird

## Purpose

Mockingbird is a local-first Windows 11 tray application that gives Claude Code a voice. It synthesizes spoken audio from text, on-device, with no internet, no per-character billing, and no LLM-provider dependency. Its single primary consumer is Claude Code: short notification cues when a task finishes or input is required, and longer read-aloud passes so the user can move around the house with a headset instead of staring at a terminal. Voice diversity is the key feature — different voices route to different parallel Claude sessions so the user can tell sessions apart by ear before they even hear the words.

Mockingbird is the dedicated TTS sibling to [WhisperHeim](../../tooling/WhisperHeim/) (which handles dictation and call transcription). The two apps share UI technology and design language but are deployed independently. Mockingbird *replaces* the Text-to-Speech page that was previously planned for WhisperHeim — TTS gets pulled out into its own focused app.

## Users

A single user (the developer) running Claude Code across multiple parallel terminals on a Windows 11 workstation. The user is German but accepts English-only voices for v1. The user is comfortable with:

- Local AI tooling (already runs WhisperHeim, Ollama)
- Recording their own voice samples
- Capturing system audio from audiobooks, games, podcasts, video clips
- Triggering speech via Claude Code hooks and via the tray UI directly

There are no other users. No multi-tenancy, no auth beyond local-machine trust, no telemetry.

## The problem

When running multiple Claude Code sessions in parallel terminals, the user has no peripheral signal to know:

- *Which* session just finished a task
- *Which* session is blocked waiting for input
- *What* the latest session output actually says, if their attention is elsewhere

Polling terminals visually breaks flow. Generic "ding" sounds don't distinguish sessions. Cloud TTS services (ElevenLabs, OpenAI) solve the speech part but cost money, leak text to a provider, fail offline, and don't let the user clone arbitrary voices from the audio sources they already have.

Mockingbird closes the loop: every Claude session can speak in a distinguishable voice, locally and instantly enough (≤2 s latency budget), with the user owning every voice profile.

## What success looks like

V1 is worth shipping when:

1. A Claude Code hook can call mockingbird with `{text, voice}` and audio plays through the user's selected output device within ~2 seconds.
2. Concurrent calls from multiple sessions queue cleanly — no overlap, no drops without intent.
3. The user can stop playback instantly with a global hotkey (double-tap LCtrl) when they want to respond.
4. The user can clone a new voice from a microphone recording or a system-audio capture in under a minute, and use it on the next request.
5. The voice library holds at least 5–10 voices reliably, and the architecture clearly accommodates growing to 20–50.
6. The tray app launches, runs unattended in the system tray, and feels like a first-party Windows app — Mica backdrop, Fluent controls, Segoe UI Variable, the WhisperHeim aesthetic.

## Non-goals

Explicitly *not* in scope for v1:

- **Multilingual voices.** Pocket-tts is English-only at launch. Architecture should not foreclose a future multi-engine path, but no second engine ships in v1.
- **Reading articles, PDFs, or arbitrary documents.** Read-aloud serves Claude Code output specifically.
- **Generating audio for personal projects** (narration, podcasts, game characters). May come later. Not v1.
- **Multi-user / multi-machine deployment.** Strictly local to one workstation.
- **Cloud sync of voice profiles.** Voice profiles live on the local filesystem. If the user wants sync, they can put the folder under OneDrive themselves — mockingbird does not orchestrate it.
- **Full audio editing.** No clipping, denoising, or mixing UI — just enough capture controls to get a clean ~10-second sample.
- **GPU acceleration.** Pocket-tts is CPU-only by design. Don't build GPU code paths for v1.
- **Dictation, transcription, or any STT feature.** That's WhisperHeim's job.
- **Authentication or remote-access protection.** The HTTP/IPC surface is localhost-only; no token scheme in v1.
- **Voice marketplace, sharing, or any social feature.** Personal tool only.

## Ubiquitous language (seed)

| Term | Meaning |
|---|---|
| **Voice profile** | A named, persistent representation of a voice that pocket-tts can reuse instantly. Created once from a sample clip; stored as a `.safetensors` file plus metadata. |
| **Sample clip** | The 5–20-second audio snippet used to create a voice profile. Source can be microphone, system audio (loopback), or imported file. |
| **Built-in voice** | A voice profile shipped with pocket-tts itself — available out of the box. |
| **Cloned voice** | A user-created voice profile, derived from a sample clip the user provided. |
| **Speak request** | An incoming call from Claude Code (or the tray UI) carrying text + voice ID. The unit of work mockingbird queues, synthesizes, and plays. |
| **Speak queue** | The FIFO buffer of pending speak requests. New requests join the tail. The currently-playing request is the head; finishing or being stopped advances the queue. |
| **Stop signal** | A user action (double-tap LCtrl) that immediately halts the current playback and *also* drains the queue (TBD: drain-or-keep is an open question — see below). |
| **Loopback capture** | Recording the audio that's currently playing on the user's output device (the system-audio source). Implemented via WASAPI loopback. |
| **First-chunk latency** | Time from speak request received to the first audio sample reaching the speakers. Target ≤2 s; pocket-tts claims ~200 ms. |
| **Streaming synthesis** | Producing audio in chunks as text is processed, so playback can begin before the whole utterance is rendered. Critical for read-aloud. |

## Key drivers (informs trade-offs later)

In priority order, when in doubt, optimize for:

1. **Privacy** — text never leaves the machine
2. **Offline capability** — no internet dependency at any layer
3. **LLM-provider independence** — works regardless of which model the user runs
4. **Zero recurring cost** — no subscriptions, no per-character billing
5. **Own-your-voices** — the user can clone any voice from any audio source they have

Latency is a secondary concern: ≤2 s end-to-end is acceptable. Quality matters but is bounded by pocket-tts's English-only model in v1.

## Open questions

To revisit during architecture or as the project unfolds:

- **Python-vs-C# integration shape.** Pocket-tts is a Python library; the host is C#/WPF/.NET 9. Sidecar process? Bundled Python runtime? sherpa-onnx ONNX port (Windows-tested, would let us stay in C#)? — to be resolved by the architect step.
- **Claude → mockingbird transport.** Local HTTP server? Named pipe? CLI invocation? All viable; the architect picks one.
- **Stop-signal semantics.** Does double-tap LCtrl drain the entire queue or only stop the current utterance? Keeping it ambiguous in vision; revisit on first usage.
- **Concurrent playback decoupling.** Read-aloud (long) and notification cues (short) might want different lanes — should a "task done" cue be able to barge to the front of the queue, or strictly FIFO? Decide when the user has run a few sessions in anger.
- **Voice library organization.** 20–50 voices on a flat list will work but probably want at least search/filter. Tags, categories, or favorites? Decide once we have ~15 voices.
- **Multilingual upgrade path.** Architecture should leave room for a second engine (Chatterbox if a GPU appears, Kyutai multilingual if Kyutai ships it). A "voice profile" should know which engine produced it.
- **Reuse vs duplication with WhisperHeim.** WhisperHeim's audio capture, hotkey, model-download, and tray infrastructure are reusable. Shared library? Copy-and-modify? Submodule? — out-of-scope for vision; resolve during foundation.
