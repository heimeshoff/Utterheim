---
id: 0002
title: Run pocket-tts as a managed Python sidecar over loopback HTTP
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-002, main-007, main-009]
related_research: [kyutai-tts-2026-05-01]
---

# ADR 0002: Run pocket-tts as a managed Python sidecar over loopback HTTP

## Context

Pocket-tts is a Python package (PyPI `pocket-tts`, MIT/CC-BY-4.0, ~100M params, CPU-only, ~200ms first-chunk). It is English-only at v1. Kyutai documents a built-in FastAPI server (`pocket-tts serve`). The host language for mockingbird is C#/WPF/.NET 9.

Three integration shapes are viable:

- **(a)** Python sidecar process managed by the C# host.
- **(b)** Bundled Python runtime via Python.NET / pythonnet, in-process.
- **(c)** sherpa-onnx ONNX port — keep everything in C#.

The vision's hard constraint is that the tray app must be reliable, fast to launch, and updatable without Python-experience friction. The biggest risk is Windows compatibility of pocket-tts itself: Kyutai's own performance numbers are macOS-only.

## Decision

Run pocket-tts as a Python sidecar process that mockingbird supervises:

- Bundle Python 3.12 embeddable + a prepared site-packages folder (containing `pocket-tts`, torch CPU, deps) inside the mockingbird install directory, under `runtime\python\`.
- On first launch, mockingbird verifies `python.exe` and the pocket-tts package are present; if not, it runs the install script (lazy bootstrap).
- On every launch (or on first speak request), spawn `runtime\python\python.exe -m pocket_tts.server --host 127.0.0.1 --port 0`. Bind only to loopback.
- The C# host owns the process lifecycle: graceful shutdown on tray exit, restart on crash with backoff, structured logging of stdout/stderr to Serilog.
- The synthesis engine is wrapped behind an internal `ITtsEngine` interface so a future second engine (sherpa-onnx ONNX, Chatterbox, Kyutai multilingual) can slot in.
- The voice profile model treats `.safetensors` files as opaque artefacts produced and consumed by the sidecar; mockingbird only handles file paths and metadata.

## Consequences

### Positive

- Canonical pocket-tts; updates are just `pip install -U` inside the bundled venv.
- Process isolation: a sidecar crash does not take down the tray app.
- Streaming is well-defined (HTTP chunked / WebSocket from FastAPI server).
- The engine interface leaves a clear path to add a second engine later.

### Negative

- Distribution size grows substantially (Python + torch CPU is ~500MB-1GB on disk).
- Cold-start sidecar spawn is slower than in-process; mitigated by keeping the sidecar resident (warm) for the lifetime of the tray app.
- We own the Python venv dependency tree on Windows. Real risk; budget time for it.

### Neutral

- The sidecar can be managed independently for debugging — start it manually and point mockingbird at it for development.

## Alternatives considered

- **(b) Python.NET / pythonnet** — rejected: in-process Python crashes the tray; more fragile interop; harder to upgrade pocket-tts independently.
- **(c) sherpa-onnx ONNX port (community)** — rejected as primary because the port may lag official, and the research recommends reading `pocket_tts/server.py` for streaming details. Kept as a documented fallback if Windows packaging proves intractable; the engine interface guarantees we can switch with bounded blast radius.
- **Cloud TTS (ElevenLabs/OpenAI)** — rejected by vision (privacy, offline, cost).

## References

- Kyutai TTS research: `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`
- Vision: `.agenthoff/vision.md`
- Pocket-tts repo: `kyutai-labs/pocket-tts` (GitHub)
