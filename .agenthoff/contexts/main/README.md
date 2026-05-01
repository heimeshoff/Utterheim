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

**Rule:** every frontend-bearing task in this BC `depends_on` the styleguide task **`main-010`** (`done/main-010-styleguide.md`, artefact at [`docs/styleguide.md`](../../../docs/styleguide.md)). The styleguide artefact **now exists** but **requires user sign-off** before frontend feature tasks may be promoted from `backlog/` to `todo/`. The sign-off mechanism is the placeholder line at the bottom of `docs/styleguide.md`: until it is replaced with a `signed-off-by:` entry, the gate remains closed. This protects the "feels like a first-party Windows app — Mica backdrop, Fluent controls, Segoe UI Variable, the WhisperHeim aesthetic" success criterion in the vision.

Note: the walking skeleton (`main-009`) is exempt — its UI is a placeholder shell whose purpose is foundation, not presentation. Real frontend feature work waits for `main-010` sign-off.

## Code structure

The walking skeleton (main-009) materialises ADRs 0001–0008 as code. Top-level
layout:

```
mockingbird.sln
src\
  Mockingbird\                        WPF tray app, net9.0-windows x64
    EntryPoint.cs                     composition root + IHost lifecycle
    App.xaml(.cs)                     WPF Application + WPF-UI theme dictionary
    Views\
      MainWindow.xaml(.cs)            Mica FluentWindow + tray:NotifyIcon menu
      BootstrapDialog.xaml(.cs)       first-run dialog (placeholder in v1)
    Services\
      Tts\
        ITtsEngine.cs                 the seam every TTS engine plugs into
        StubTtsEngine.cs              440 Hz test tone (replaced by main-011)
      Speak\
        SpeakRequest.cs               unit of work
        SpeakQueue.cs                 Channel<T> worker (ADR 0007, 0004)
        AudioPlayer.cs                NAudio WaveOutEvent wrapper
      Http\SpeakServer.cs             Kestrel minimal API on 127.0.0.1:7223 (ADR 0003)
      Hotkey\
        NativeMethods.cs              copied from WhisperHeim @ 911bff0
        DoubleTapDetector.cs          mockingbird-specific LCtrl gesture (ADR 0006)
      Settings\DataPathService.cs     ADR 0005 path layout (adapted from WhisperHeim)
    appsettings.json                  default port + hotkey window
  Mockingbird.Cli\                    mockingbird-speak — single-file CLI wrapper
README.md
```

Path layout at runtime (per ADR 0005):

```
%APPDATA%\Mockingbird\bootstrap.json    machine-local data-path pointer
%LOCALAPPDATA%\Mockingbird\
  logs\mockingbird-YYYYMMDD.log         Serilog rolling sink (ADR 0008)
  runtime\python\                       (main-011 will populate this)
  models\pocket-tts\                    (main-011 will populate this)
  cache\
  bootstrap-state.json                  first-run completion marker
<dataPath>\voices\library.json          empty list in v1 skeleton
```

## Notes for the architect

ADRs 0001–0008 are committed and the walking skeleton (main-009) has
materialised them as code. The synthesis engine ships **stubbed** for the
skeleton (440 Hz test tone via `StubTtsEngine`); the real pocket-tts sidecar
bootstrap is tracked as `main-011-pocket-tts-real-bootstrap.md` in the
backlog. Every other architectural seam — HTTP, queue, NAudio playback,
hotkey, tray, logging, path layout, CLI wrapper — is real and end-to-end
verified.

Historical: the boundary analysis surfaced four architectural decisions which
have now been resolved by the ADRs above:

1. Python-vs-C# integration shape for pocket-tts → ADR 0002 (Python sidecar).
2. Claude-to-mockingbird transport → ADR 0003 (HTTP loopback on :7223).
3. Stop-signal semantics → ADR 0004 (drain queue by default).
4. WhisperHeim reuse form → ADR 0006 (copy-and-modify in v1).
