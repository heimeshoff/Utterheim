# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-05-01 14:43 -- Work session ended

**Type:** Work / Session end
**Completed:** 2 (main-011, main-012)
**Bounced:** 0
**Failed:** 0
**Commits:** 3 (sign-off+promote 5ffa4c9, main-011 061c51b, main-012 d5b683f)
**New backlog items:** none

todo/, doing/, and backlog/ are all empty. Mockingbird's foundation phase is complete: real pocket-tts engine wired (user-verifiable on next restart with ~700 MB first-run download), speaking-person logo rasterised to all required sizes + .ico (visible in tray + taskbar after restart). Next phase is feature work — Speak page, Voices page, Settings, About — driven via /agenthoff:model captures from the user.

---

## 2026-05-01 14:42 -- Task completed: main-012 - Logo rasterisation

**Type:** Work / Task completion
**Task:** main-012 - Rasterise the speaking-person logo to PNG sizes + .ico for tray
**Summary:** Generated PNG sizes 16/24/32/48/64/128/256/512 + multi-resolution mockingbird.ico (16/24/32/48/64/128/256 layers, PNG-compressed) into assets/branding/ via a one-shot SkiaSharp + Svg.Skia helper at Tools/RasteriseLogo/ (kept standalone, not in mockingbird.sln). Wired the .ico as ApplicationIcon, packaged Resource, FluentWindow Icon, and tray:NotifyIcon Icon. Build clean (0/0). User-verifiable: speaking-person silhouette now appears in the tray + Explorer + taskbar after restart.
**Commit:** d5b683f
**Files changed:** 14
**ADRs written:** none

---

## 2026-05-01 14:31 -- Batch started: [main-012]

**Type:** Work / Batch start
**Tasks:** main-012 - Rasterise the speaking-person logo to PNG sizes + .ico
**Parallel:** no (1 worker)

---

## 2026-05-01 14:30 -- Task completed: main-011 - Real pocket-tts engine

**Type:** Work / Task completion
**Task:** main-011 - Real pocket-tts engine — Python sidecar bootstrap and PocketTtsEngine
**Summary:** Probe-installed pocket-tts==2.0.0 cleanly into a fresh Python 3.12 venv on Windows 11 — ADR 0002 path validated. Wrote PocketTtsEngine (POST /tts, strips WAV header, streams 24 kHz mono 16-bit PCM via IAsyncEnumerable<byte[]>), SidecarHost (supervises python.exe with port-banner parsing, /health polling, capped-backoff restarts, clean shutdown), PythonRuntimeBootstrapper (downloads Python 3.12.7 embeddable, enables site, bootstraps pip, installs pocket-tts>=2.0,<3, persists state across restarts), real BootstrapDialog with progress + cancel + retry, EntryPoint DI swap (StubTtsEngine gated behind MOCKINGBIRD_USE_STUB_ENGINE=1), SpeakServer /status reports sidecar state. Build clean (0/0). User-verifiable acceptance criteria pending first run with real download (~700 MB).
**Commit:** 061c51b
**Files changed:** 9
**ADRs written:** none

---

## 2026-05-01 14:19 -- Batch started: [main-011]

**Type:** Work / Batch start
**Tasks:** main-011 - Real pocket-tts engine — Python sidecar bootstrap and PocketTtsEngine
**Parallel:** no (1 worker — large scope; main-012 demoted to next batch due to csproj conflict)

---

## 2026-05-01 14:19 -- Sign-off + promote: main-011, main-012

**Type:** Model / Promote (post sign-off)
**Summary:** User confirmed the walking skeleton works (440 Hz tone played end-to-end via /speak, /stop, double-tap LCtrl) and approved the styleguide. docs/styleguide.md sign-off line replaced with `signed-off-by: Marco Heimeshoff on 2026-05-01`. BC README updated to reflect the OPEN gate state. Both backlog items (main-011 real engine, main-012 logo raster) promoted from backlog/ to todo/, frontmatter `status: todo`. Next: dispatch work waves for both.

---

## 2026-05-01 13:20 -- Work session ended

**Type:** Work / Session end
**Completed:** 10 (main-001 through main-010)
**Bounced:** 0
**Failed:** 0
**Commits:** 11 (1 bootstrap + 8 ADR commits + 1 walking-skeleton + 1 styleguide)
**New backlog items:** main-011 (real pocket-tts engine bootstrap), main-012 (logo rasterisation)

All 8 foundation ADRs committed; walking skeleton builds clean and smoke-tested live (HTTP + queue + audio + hotkey end-to-end with stubbed engine); styleguide artefact produced and awaiting user sign-off. Mockingbird is ready for the user to (a) sign off on docs/styleguide.md, (b) decide when main-011 (real engine) and main-012 (final logo raster) move from backlog to todo via /agenthoff:model.

---

## 2026-05-01 13:18 -- Task completed: main-010 - Styleguide

**Type:** Work / Task completion
**Task:** main-010 - Styleguide — adapt WhisperHeim's design language and the speaking-person logo
**Summary:** Produced docs/styleguide.md (inherited-from-WhisperHeim section + explicit divergences + reusable component map + sign-off placeholder), placeholder speaking-person SVG at assets/branding/mockingbird-logo.svg (clearly marked PLACEHOLDER), MainWindow.xaml updated to render the silhouette inline + show "Local voices for Claude Code" tagline. Build remains clean. **GATE STATE: artefact ready, awaiting user sign-off before any frontend feature task can be promoted.**
**Commit:** 44ce127
**Files changed:** 5
**ADRs written:** none
**New backlog items:** main-012 - Rasterise the speaking-person logo to PNG sizes + .ico

---

## 2026-05-01 13:15 -- Batch started: [main-010]

**Type:** Work / Batch start
**Tasks:** main-010 - Styleguide (WhisperHeim design + speaking-person logo)
**Parallel:** no (1 worker; sign-off gate)

---

## 2026-05-01 13:13 -- Task completed: main-009 - Walking skeleton

**Type:** Work / Task completion
**Task:** main-009 - Walking skeleton — Claude hook → HTTP → sidecar → audio out
**Summary:** Walking skeleton stands up end-to-end on .NET 9 / WPF / x64: WPF-UI Mica tray window with show/stop/exit menu, Kestrel minimal API on 127.0.0.1:7223 (POST /speak, /stop, GET /voices, /status), Channel<T>-based SpeakQueue with stop-and-drain semantics, NAudio playback, low-level keyboard hook for double-tap LCtrl, ADR-0005 path layout, Serilog rolling file sink, single-file mockingbird-speak CLI wrapper, and copy-and-modify provenance headers from WhisperHeim @ 911bff0. The TTS engine is stubbed (440 Hz test tone via StubTtsEngine behind ITtsEngine) — real pocket-tts sidecar bootstrap captured as main-011 in backlog. Build clean, smoke test passed.
**Commit:** 4becb42
**Files changed:** 26 (plus moved task file + leftover todo/ deletions cleanup)
**ADRs written:** none (skeleton materialises ADRs 0001–0008 as code)
**New backlog items:** main-011 - Real pocket-tts engine bootstrap

---

## 2026-05-01 13:00 -- Batch started: [main-009]

**Type:** Work / Batch start
**Tasks:** main-009 - Walking skeleton (spike)
**Parallel:** no (1 worker, large scope)
**Note:** Orchestrator scoped the spike to a stubbed-engine variant — `ITtsEngine` returns a generated test tone instead of real pocket-tts output. Real engine bootstrap moves to a new follow-up task (`main-011`) so the skeleton can validate architecture fast without bundling a multi-hundred-MB Python + model download into one worker session.

---

## 2026-05-01 13:00 -- Task completed: main-007 - Queue mechanism

**Type:** Work / Task completion
**Task:** main-007 - Speak queue lives in the C# host as a Channel<T>
**Summary:** Wrote ADR 0007 locking the speak queue placement: C# host owns the FIFO Channel<SpeakRequest> while the Python sidecar remains stateless per-utterance.
**Commit:** 5c8cdcc
**Files changed:** 1
**ADRs written:** 0007-queue-channel-in-host.md

---

## 2026-05-01 12:58 -- Batch started: [main-007]

**Type:** Work / Batch start
**Tasks:** main-007 - Speak queue as Channel<T> in C# host
**Parallel:** no (1 worker, deps just satisfied)

---

## 2026-05-01 12:58 -- Task completed: main-008 - Cross-cutting concerns

**Type:** Work / Task completion
**Task:** main-008 - Cross-cutting (Serilog, fail-loud, model bootstrap, distribution)
**Summary:** Wrote ADR 0008 capturing four cross-cutting decisions (Serilog logging with sidecar redirect sink, fail-loud-to-tray error philosophy, WhisperHeim-style model + runtime bootstrap dialog, self-contained zip distribution).
**Commit:** e4a6797
**Files changed:** 1
**ADRs written:** 0008-cross-cutting-concerns.md

---

## 2026-05-01 12:57 -- Batch started: [main-008]

**Type:** Work / Batch start
**Tasks:** main-008 - Cross-cutting (Serilog, fail-loud, model bootstrap, distribution)
**Parallel:** no (1 worker, solo dispatch)

---

## 2026-05-01 12:57 -- Task completed: main-006 - WhisperHeim reuse form

**Type:** Work / Task completion
**Task:** main-006 - Reuse WhisperHeim infrastructure via copy-and-modify in v1
**Summary:** Recorded ADR 0006 documenting the decision to reuse WhisperHeim infrastructure via copy-and-modify in v1 (with provenance headers and CHANGELOG entries), deferring shared-library extraction until both apps ship v1.
**Commit:** d3137e3
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
