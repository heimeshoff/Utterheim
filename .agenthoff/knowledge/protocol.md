# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-05-03 23:58 -- Task completed: main-022 - Tray Exit leaves the python.exe sidecar alive as a zombie

**Type:** Work / Task completion
**Task:** main-022 - Tray Exit leaves the python.exe sidecar alive as a zombie
**Summary:** Tray Exit now reaps the entire python sidecar process tree by binding every spawned python.exe to a Win32 Job Object with KILL_ON_JOB_CLOSE; an explicit _shuttingDown flag also prevents the supervisor from respawning the sidecar mid-shutdown.
**Commit:** 8a17ff9
**Files changed:** 6 (incl. moved task file + ADR + BC README)
**ADRs written:** 0012-sidecar-jobobject-kill-on-close.md

---

## 2026-05-03 23:55 -- Batch started: [main-022]

**Type:** Work / Batch start
**Tasks:** main-022 - Tray Exit leaves the python.exe sidecar alive as a zombie
**Parallel:** no (1 worker — only ready task; main-018 + main-019 blocked on this)

---

## 2026-05-03 23:50 -- Model / Promoted: main-022 - Tray Exit leaves the python.exe sidecar alive as a zombie

**Type:** Model / Promote
**BC:** main
**From → To:** backlog → todo
**Why now:** main-022 already carried 5 concrete acceptance criteria, identified affected files (`SidecarProcessManager`, `App.xaml.cs`, supervisor), and a three-point fix plan. No unmet dependencies. Promoting unblocks main-018 → main-019.

---

## 2026-05-03 23:35 -- Bugs filed: main-022, main-023 (from main-018 verification)

**Type:** Work / Verification finding → bugs filed
**BC:** main
**Trigger:** User ran main-018 first-run verification post main-021 fix. Bootstrap, short speak, sidecar logs, and streaming-correctness criteria pass. Two breakages found.
**main-022 (blocks main-018):** Tray Exit leaves `python.exe` sidecar alive in Task Manager. Acceptance criterion 6 hard-fails. Likely needs `Process.Kill(entireProcessTree: true)`, a graceful-then-hard shutdown sequence, and a "shutting down" flag to suppress the auto-restart watchdog during host exit.
**main-023 (does not block main-018):** ~9 s first-chunk latency on 200-word input vs ≤2 s vision target. Streaming itself works (criterion 3 passes); this is a vision-level perf concern. Probable root cause: whole-text preprocessing before first chunk, or non-streaming HTTP transport.
**Decision (criterion 4):** Not filing the auto-restart-race observability gap. The resilience contract is satisfied; visibility into the brief degraded window is debugging convenience, not user value.
**main-018 status:** Stays in `todo/` with `depends_on: [main-011, main-021, main-022]` and an updated `## Outcome (partial)` block tracking 5/7 criteria + the two main-021 spot-checks.

---

## 2026-05-03 23:13 -- Work session ended

**Type:** Work / Session end
**Completed:** 1
**Bounced:** 0
**Failed:** 0
**Commits:** 1
**Halted on:** main-018 unblocked but cannot be auto-dispatched — it is a human first-run verification chore (real-audio playback, curl probes, log inspection). main-019 stays transitively blocked behind main-018.

---

## 2026-05-03 23:12 -- Task completed: main-021 - Bootstrap skips pocket-tts install when state file outlives runtime; smoke-test stderr is invisible

**Type:** Work / Task completion
**Task:** main-021 - Bootstrap skips pocket-tts install when state file outlives runtime; smoke-test stderr is invisible
**Summary:** Bootstrap now treats on-disk sentinel files as authoritative — step 3 verifies `pocket_tts/__init__.py` before trusting the JSON flag, upstream re-runs cascade-reset downstream flags, and subprocess stderr is captured + replayed at Error on non-zero exit so failures are diagnosable in both the file log and the dialog.
**Commit:** 75650d9
**Files changed:** 3
**ADRs written:** 0011-bootstrap-state-reconciliation.md

---

## 2026-05-03 23:05 -- Batch started: [main-021]

**Type:** Work / Batch start
**Tasks:** main-021 - Bootstrap skips pocket-tts install when state file outlives runtime; smoke-test stderr is invisible
**Parallel:** no (1 worker)

---

## 2026-05-03 22:34 -- Model / Promoted: main-021

**Type:** Model / Promote
**BC:** main
**From → To:** backlog → todo
**Summary:** Promoted main-021 (bootstrap skips pocket-tts install when state file outlives runtime; smoke-test stderr is invisible). Required to unblock main-018 verification. No `depends_on`, so it is the only ready task in the BC.

---

## 2026-05-03 22:30 -- Bug filed: main-021 (bootstrap skips pocket-tts install)

**Type:** Work / Verification finding → bug filed
**BC:** main
**Trigger:** User attempted main-018 first-run verification; bootstrap dialog failed with `Python smoke test exited with code 1` (eight retries logged in mockingbird-20260503.log between 22:18:04 and 22:18:36).
**Root cause (recorded in main-021):** Two defects in `PythonRuntimeBootstrapper`. (A) Step 3 `InstallPocketTts` checks only the persisted state flag, not `pocket_tts/__init__.py` on disk — so a stale `bootstrap-state.json` (e.g. after a manual wipe of `runtime/`) silently skips re-installing pocket-tts. (B) Subprocess stderr is logged at `LogDebug` so the actual Python traceback never reaches the file log; user sees only the generic exit-code-1 message.
**Action:** Filed main-021 in backlog with both fix sketches and acceptance criteria. Added main-021 to main-018's `depends_on`. main-018 stays in todo with an `## Outcome (verification paused)` block; verification cannot proceed until main-021 lands.
**Workaround for the user:** delete `bootstrap-state.json` alongside `runtime\` and `models\pocket-tts\` before relaunching.
**Worker dispatch:** none (no ready, unblocked tasks remain — main-018 is now blocked on main-021, main-019 transitively blocked).

---

## 2026-05-03 -- Model / Promoted: main-018 + main-019

**Type:** Model / Promote
**BC:** main
**From → To:** backlog → todo
**Summary:** Promoted main-018 (clean-machine first-run verification of main-011) and main-019 (Claude Code hook sample) — the two foundation-closing tasks that turn main-011 from "code-clean" into "actually used". Encoded ordering as a hard dependency: main-019 now depends on main-018 (its troubleshooting acceptance criterion references failure modes from the verification run); main-018 blocks main-019.

---

## 2026-05-02 10:08 -- Work session ended

**Type:** Work / Session end
**Completed:** 1
**Bounced:** 0
**Failed:** 0
**Commits:** 1

---

## 2026-05-02 10:07 -- Task completed: main-020 - Navigation shell — wpfui NavigationView with four-page skeleton

**Type:** Work / Task completion
**Task:** main-020 - Navigation shell — wpfui NavigationView with four-page skeleton
**Summary:** Replaced placeholder MainWindow content with a wpfui NavigationView shell hosting four stub pages (Speak / Voices / Settings / About), wired CommunityToolkit.Mvvm and an IPageService→DI adapter, and added a persistent status footer driven by SidecarHost.StateChanged.
**Commit:** a19e9b3
**Files changed:** 25
**ADRs written:** 0009, 0010 (drafts finalised in-place; not authored by worker)

---

## 2026-05-02 09:57 -- Batch started: [main-020]

**Type:** Work / Batch start
**Tasks:** main-020 - Navigation shell — wpfui NavigationView with four-page skeleton
**Parallel:** no (1 worker)

---

## 2026-05-01 16:30 -- Model / Refined: main-020 - Navigation shell (user pass)

**Type:** Model / Refine
**BC:** main
**Status after:** todo (was demoted to backlog mid-refinement, then re-promoted
once user resolved the open questions)
**Summary:** Two-pass refinement on main-020. Orchestrator pass surfaced six
opens (wpfui v3 lifecycle interface, page service choice, MVVM toolkit,
engine-status interim visibility, brand-mark extraction, titlebar text) and
demoted the task back to `backlog/` pending answers. Marco delegated the
calls back ("you decide" on most). Decisions: implement both
`INavigableView<T>` (typed VM) and `INavigationAware` (lifecycle hooks);
use wpfui's built-in `IPageService`/`INavigationService` with a thin
adapter; **adopt CommunityToolkit.Mvvm** (override of orchestrator's bare
INPC pick — source-gen attributes scale better across 4+ pages); **ship a
persistent status footer** showing HTTP endpoint + engine state (no signal
regression while main-017 is unbuilt); brand-mark extraction punted to
worker time; static `TitleBar` text. Re-promoted to `todo/` once all six
landed.
**ADRs written:** 0010 — rewritten in place from "bare INPC" to
"`CommunityToolkit.Mvvm` source generators". Filename retains its slug.
**Side effects:** main-013 received a small editorial fix —
`INavigableView<SpeakPageViewModel>.OnNavigatedTo()` references corrected
to `INavigationAware.OnNavigatedTo()` (intent unchanged, interface name
was wrong).

---

## 2026-05-01 15:50 -- Model / Refined: main-013 - Speak page (user pass)

**Type:** Model / Refine
**BC:** main
**Status after:** backlog (still blocked on main-020)
**Summary:** Marco walked through the eight open questions in person.
Confirmed Q1 FIFO, Q2 SpeakService extraction, Q3 navigation-shell split,
Q5(a) re-synthesize on Save, Q6 four-state status line. Punted Q4 back
("you decide") — kept VoiceCatalog (in-process) over HTTP-loopback for
cleaner startup ordering and symmetry with SpeakService. **Overrode Q7**:
the picker now reads from a new `UserSettings.DefaultVoiceId` (with `alba`
fallback) instead of always picking the first voice. The settings *storage*
ships in main-013 (new `Services\Settings\UserSettings.cs` + forward-compat
`settings.json`); the *UI to mutate* moves to main-016.
**Side effects:** main-016's forward-link rewritten — no longer "add the
storage and the UI", now "extend the existing UserSettings with the
default-voice dropdown plus the other settings slots".

---

## 2026-05-01 15:30 -- Model / Refined: main-013 - Speak page

**Type:** Model / Refine
**BC:** main
**Status after:** backlog (blocked on main-020)
**Summary:** Resolved all eight open questions on the Speak page task — FIFO (no
barge), extract `SpeakService` and `VoiceCatalog` so HTTP and UI share the seam
structurally, render-on-Save (textbox is source of truth), four-state status
line with transient `stopped`, alphabetical default voice (alba), refresh on
`OnNavigatedTo()`. The navigation shell was split out into a separate task to
keep main-013 page-shaped and unblock the other page tasks symmetrically.
**Split into:** main-020 (navigation shell, promoted directly to todo/)
**ADRs written:** 0009 (page navigation via wpfui NavigationView with
INavigableView pages)
**Side effects:** main-014, main-016, main-017 all gained `main-020` in their
`depends_on`. main-016 picked up a forward-link for the persisted "default
voice" setting deferred from Q7.

---

## 2026-05-01 15:05 -- Model / Captured: main-013..main-019 (v1 feature batch)

**Type:** Model / Capture
**BC:** main
**Filed to:** backlog
**Summary:** Captured the seven natural next steps after the foundation phase as backlog items: page set (main-013 Speak, main-014 Voices, main-015 voice cloning, main-016 Settings, main-017 About) all gated by the now-OPEN styleguide (main-010); main-018 chore to close out main-011's pending user-verifiable acceptance criteria on a clean machine; main-019 Claude Code hook sample so the speak endpoint actually gets used. Suggested run order: main-018 first (verify foundation), main-019 next (deliver v1 payoff), then page set in styleguide-canonical order. Tasks are intentionally lean — refine via /agenthoff:model before promotion.

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
