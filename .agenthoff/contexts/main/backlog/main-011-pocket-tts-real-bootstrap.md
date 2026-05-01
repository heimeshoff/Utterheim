---
id: main-011
title: Real pocket-tts engine — Python sidecar bootstrap and PocketTtsEngine
status: backlog
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-009]
blocks: []
tags: [foundation, engine, sidecar, bootstrap]
---

## Why

The walking skeleton (main-009) ships with a stubbed `ITtsEngine` that plays a
440 Hz test tone instead of synthesised speech. Every architectural seam is in
place — HTTP, queue, NAudio playback, hotkey, tray, logging, voice list — but
the user never actually hears Claude's voice come through. This task replaces
the stub with the real pocket-tts engine so mockingbird becomes useful.

This is also where the heavy first-run UX lives: bundling embeddable Python,
pip-installing pocket-tts and its torch CPU dependency, downloading the model
weights. That bootstrap was deliberately deferred from main-009 to keep the
skeleton tractable.

## What

1. **Bundle / prepare embeddable Python** at `%LOCALAPPDATA%\Mockingbird\runtime\python\`:
   - First-launch bootstrap dialog (replacing the placeholder shipped in main-009)
     downloads Python 3.12 embeddable, extracts to `runtime\python\`, and pip-installs
     `pocket-tts` plus deps into a venv-style site-packages folder. Mirror
     WhisperHeim's `ModelDownloadDialog` UX (per ADR 0008) — per-step progress,
     cancel, retry, persisted `bootstrap-state.json`.
   - On subsequent launches: verify integrity, skip the dialog.

2. **Trigger pocket-tts model weights download** on first launch (the actual
   `kyutai/pocket-tts` `.safetensors` weights). Per the kyutai-tts research
   report, the package handles this on first synthesis call — just need to run
   one synthesis through and surface progress to the user.

3. **`PocketTtsEngine : ITtsEngine`** that:
   - On startup, spawns `runtime\python\python.exe -m pocket_tts.server --host 127.0.0.1 --port 0`,
     reads the assigned port from stdout (or a hand-off file), bound loopback only.
   - Health-checks the sidecar before declaring ready (`GET /healthz` if exposed,
     else a benign synthesis call).
   - Streams audio chunks from the sidecar's HTTP synthesis endpoint into the
     `IAsyncEnumerable<byte[]>` the existing `SpeakQueue` consumes (no API
     changes — main-009's `ITtsEngine` interface is the contract).
   - Surfaces the eight pocket-tts built-ins (`alba`, `marius`, `javert`, `jean`,
     `fantine`, `cosette`, `eponine`, `azelma`) via `ListVoicesAsync()` with
     `engine: "pocket-tts"`, `isBuiltIn: true`.
   - Captures sidecar stdout/stderr into Serilog as `sidecar`-tagged events
     (per ADR 0008).
   - Tears down cleanly when the host stops (graceful TERM, then kill on timeout).
   - Restarts on crash with backoff (per ADR 0002).

4. **Wire-up in DI** (`EntryPoint.cs`): swap `services.AddSingleton<ITtsEngine, StubTtsEngine>()`
   for `services.AddSingleton<ITtsEngine, PocketTtsEngine>()`. Optionally keep the
   stub gated behind an env flag (`MOCKINGBIRD_USE_STUB_ENGINE=1`) for offline
   testing.

5. **Output sample rate**: pocket-tts produces at its own native rate; the
   `AudioPlayer` already adapts via `ITtsEngine.OutputFormat`. Confirm the rate
   from `pocket_tts/server.py` and update `PocketTtsEngine.OutputFormat`
   accordingly (likely 24000 or 22050 Hz mono PCM).

## Acceptance criteria

- [ ] On a clean machine with no prior install, launching mockingbird shows
  a bootstrap dialog with real download progress; the dialog completes and
  hands off to the tray app.
- [ ] `curl -X POST http://127.0.0.1:7223/speak -d '{"text":"Hello, this is mockingbird.","voice":"alba"}'`
  produces audible speech in the alba voice through the default output device
  within ~2 seconds (vision target).
- [ ] `GET /voices` returns the eight pocket-tts built-ins (`alba`, `marius`,
  `javert`, `jean`, `fantine`, `cosette`, `eponine`, `azelma`) with
  `engine: "pocket-tts"` and `isBuiltIn: true`.
- [ ] Streaming is observable: a long text (~200 words) starts playing audio
  before the full synthesis completes.
- [ ] `GET /status` reports `sidecar.state = "running"` (or similar) and
  `sidecar.healthy = true` once warm; reflects degraded state during restart.
- [ ] Sidecar stdout/stderr appear in `mockingbird-YYYYMMDD.log` with a
  `sidecar` source tag.
- [ ] Closing the tray's "Exit" terminates the sidecar; no zombie `python.exe`
  remains.
- [ ] Stub engine either removed entirely OR gated behind `MOCKINGBIRD_USE_STUB_ENGINE=1`.

## Out of scope

- Voice cloning UI (mic / loopback capture, `pocket-tts export-voice` wiring) —
  separate feature, depends on main-010 styleguide.
- Voice library management UI (CRUD over `library.json`, per-voice metadata
  editing) — separate feature, depends on main-010.
- Sherpa-onnx ONNX fallback path — kept as documented fallback per ADR 0002,
  not wired in this task.
- Auto-update of pocket-tts package — manual `pip install -U` for v1.

## Notes

- Reference: ADR 0002 (Python sidecar shape), ADR 0008 (model bootstrap UX),
  the kyutai-tts research report at `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`,
  and main-009's `StubTtsEngine` for the API contract every engine must satisfy.
- The skeleton's audio path expects raw PCM chunks. If pocket-tts's HTTP
  endpoint emits a streaming WAV / Opus / MP3 encoding, decode in the engine
  before yielding so `AudioPlayer` doesn't need to change.
- Validate `pip install pocket-tts` in a fresh Windows 11 venv as the very first
  step (per the original main-009 acceptance note that was deferred). If
  pocket-tts genuinely cannot install on Windows, ADR 0002 needs revisiting
  *before* this task continues — fall back to the sherpa-onnx ONNX path.
- Does NOT depend on main-010 (styleguide). This task is engine work, not
  frontend; the bootstrap dialog can stay utilitarian and inherit main-010's
  styling once both are merged.
