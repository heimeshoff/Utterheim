# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-05-01 12:57 -- Task completed: main-006 - WhisperHeim reuse form

**Type:** Work / Task completion
**Task:** main-006 - Reuse WhisperHeim infrastructure via copy-and-modify in v1
**Summary:** Recorded ADR 0006 documenting the decision to reuse WhisperHeim infrastructure via copy-and-modify in v1 (with provenance headers and CHANGELOG entries), deferring shared-library extraction until both apps ship v1.
**Files changed:** 1
**ADRs written:** 0006-whisperheim-reuse-copy-and-modify.md

---

## 2026-05-01 12:56 -- Task completed: main-005 - Voice persistence layout

**Type:** Work / Task completion
**Task:** main-005 - Voice profiles as folder-per-voice + library.json index
**Summary:** Recorded ADR 0005 codifying voice profile persistence as folder-per-voice under `<dataPath>\voices\<voice-id>\` plus a single library.json master index, with WhisperHeim-style path layering and a fixed meta.json schema.
**Commit:** e169347
**Files changed:** 1
**ADRs written:** 0005-voice-persistence-layout.md

---

## 2026-05-01 12:55 -- Task completed: main-004 - Stop signal semantics

**Type:** Work / Task completion
**Task:** main-004 - Stop signal drains the queue by default (configurable)
**Summary:** Recorded ADR 0004 establishing that the stop signal cancels current utterance and drains the pending speak queue by default, with a tray-UI toggle for "stop current only". Unblocks queue mechanism (main-007) and HTTP /stop endpoint implementation.
**Commit:** 47230fb
**Files changed:** 1
**ADRs written:** 0004-stop-drains-queue.md

---

## 2026-05-01 12:53 -- Batch started: [main-004, main-005, main-006]

**Type:** Work / Batch start
**Tasks:** main-004 - Stop signal semantics, main-005 - Voice persistence layout, main-006 - WhisperHeim reuse form
**Parallel:** yes (3 workers)

---

## 2026-05-01 12:53 -- Task completed: main-003 - Claude transport HTTP

**Type:** Work / Task completion
**Task:** main-003 - Expose the speak endpoint over loopback HTTP (JSON)
**Summary:** Recorded ADR 0003 selecting loopback HTTP/JSON on 127.0.0.1:7223 as the speak endpoint transport, with a mockingbird-speak CLI wrapper for hook ergonomics. No code written; main-009 is now unblocked on this dimension.
**Commit:** b8d1fcc
**Files changed:** 1
**ADRs written:** 0003-claude-transport-http.md

---

## 2026-05-01 12:52 -- Task completed: main-002 - Pocket-tts sidecar

**Type:** Work / Task completion
**Task:** main-002 - Run pocket-tts as a managed Python sidecar over loopback HTTP
**Summary:** Recorded ADR 0002 selecting a managed Python sidecar (loopback HTTP) as the pocket-tts integration shape, with sherpa-onnx ONNX kept as a documented fallback behind an ITtsEngine seam. No code changes; bootstrap deferred to main-009.
**Commit:** de54a8b
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
