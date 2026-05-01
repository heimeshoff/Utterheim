# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-05-01 12:52 -- Task completed: main-002 - Pocket-tts sidecar

**Type:** Work / Task completion
**Task:** main-002 - Run pocket-tts as a managed Python sidecar over loopback HTTP
**Summary:** Recorded ADR 0002 selecting a managed Python sidecar (loopback HTTP) as the pocket-tts integration shape, with sherpa-onnx ONNX kept as a documented fallback behind an ITtsEngine seam. No code changes; bootstrap deferred to main-009.
**Files changed:** 1
**ADRs written:** 0002-pocket-tts-python-sidecar.md

---

## 2026-05-01 12:51 -- Task completed: main-001 - Confirm stack

**Type:** Work / Task completion
**Task:** main-001 - Confirm and document the .NET 9 / WPF / WPF-UI / Win x64 stack
**Summary:** Recorded the .NET 9 / WPF / WPF-UI / Windows x64 stack as ADR 0001 (scope global, status accepted), mirroring WhisperHeim's foundation. No code changes; only the ADR and task move.
**Commit:** 9232a48
**Files changed:** 1
**ADRs written:** 0001-stack-net9-wpf-x64.md

---

## 2026-05-01 12:48 -- Batch started: [main-001, main-002, main-003]

**Type:** Work / Batch start
**Tasks:** main-001 - Confirm stack, main-002 - Pocket-tts sidecar, main-003 - Claude transport HTTP
**Parallel:** yes (3 workers)

---

## 2026-05-01 -- Brainstorm: Mockingbird vision + foundation

**Type:** Brainstorm
**Outcome:** vision created
**BCs identified:** main (single bounded context — see `.agenthoff/context-map.md` for why five candidate BCs collapse into one)
**Summary:** Mockingbird is a local-first Windows 11 tray app that gives Claude Code a voice across multiple parallel terminals. Single user, single primary consumer (Claude Code), CPU-only English TTS via Kyutai's `pocket-tts` (sample-based voice cloning, ~200ms first-chunk latency). Inherits WhisperHeim's UI tech (.NET 9 / WPF / WPF-UI / Mica) and audio-capture infrastructure via copy-and-modify. Replaces the TTS page that was previously planned for WhisperHeim — TTS gets pulled out into its own focused app. Voice diversity is the core feature (different voice per Claude session = audible session disambiguation). Concurrency = FIFO queue. Stop = double-tap LCtrl, drains queue by default.
**Research conducted:** Kyutai pocket-tts capabilities and Windows-readiness — report at `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`. Confirmed pocket-tts is real, MIT/CC-BY-4.0, CPU-only, English-only at v1, native streaming. User accepted English-only constraint.
**Strategic decision:** Single BC at `contexts/main/`. Five candidate BCs (synthesis, voice-library, voice-capture, claude-bridge, tray-ui) collapsed because language, lifecycle, actors, and invariants don't diverge enough to justify split for a one-developer personal tool. Reasoning preserved in `context-map.md`.
**Architecture pass:** Architect surfaced 8 foundation decisions; ADR drafts produced and embedded in decision-task Notes.
**ADRs written:** none yet — they flow through `type: decision` tasks and will be committed by the worker as it claims each. Drafts ready as ADR 0001–0008.
**Foundation tasks emitted:**
- `main-001` Stack confirmation (.NET 9 + WPF + WPF-UI + x64)
- `main-002` Pocket-tts Python sidecar
- `main-003` Claude transport via loopback HTTP (port 7223)
- `main-004` Stop signal drains queue (configurable)
- `main-005` Voice persistence: folder-per-voice + library.json
- `main-006` WhisperHeim reuse via copy-and-modify
- `main-007` Speak queue as `Channel<T>` in C# host
- `main-008` Cross-cutting: Serilog, fail-loud, model bootstrap, zip distribution
- `main-009` Walking skeleton (spike, depends on 001–008) — first prototype
- `main-010` Styleguide (depends on 009) — adapt WhisperHeim design + speaking-person logo, **gate** for all future frontend tasks
**Protocol divergence:** brainstorm skill default would create a separate `contexts/design-system/` BC for the styleguide; that was overruled in favour of putting `main-010` inside `contexts/main/` to respect the strategic-modeler's single-BC decision. Gate semantics (every frontend task `depends_on: [main-010]`) preserved unchanged. Documented in `contexts/main/README.md`.

---
