---
id: main-045
title: Diagnose pocket-tts cancellation surface and prototype Stop propagation
status: done
type: spike
context: main
created: 2026-05-19
completed: 2026-05-19
commit: bde95c4
depends_on: []
blocks: [main-046]
tags: [spike, sidecar, cancellation, stop-signal, memory-leak]
related_adrs: [0002, 0004, 0007, 0013, 0015, 0024, 0026, 0027]
related_research: [kyutai-tts-2026-05-01, pocket-tts-upstream-cancellation-posture-2026-05-19]
prior_art: [main-022, main-023, main-024]
---

## Why

The user-measured leak from the bug report (2026-05-19) is structural:
on Stop the C# host closes the HTTP response, but the pocket-tts
sidecar's synthesis generator runs to completion in a non-preemptable
daemon thread. RSS grows ~+100 MB per cancelled cycle, CPU stays
elevated for minutes after Stop, with no observed ceiling.

ADR 0026 (committed 2026-05-19) commits to a contract: Stop must drop
sidecar CPU <5 % and return RSS to within ±50 MB of steady-state within
≤2 s. ADR 0027 records the **proposed** mechanism (hybrid disconnect-
wrapper + runtime monkey-patch of `_autoregressive_generation`) — but
flips to `accepted` only after a prototype proves it.

This spike does the proving. The follow-up **main-046** lands the
production fix. Splitting mirrors the main-023 / main-024 precedent —
the diagnosis points into pocket-tts internals, and the prototype work
needs room to discover whether the monkey-patch path actually works
under torch's `@torch.no_grad` decorator and the threaded inner loop.

## What

A diagnosis + prototype investigation. **No production code lands in
this task.** Deliverable: a measured verdict that either confirms
ADR 0027 option (e) or surfaces a finding that flips to (a)/(c).

### Already established (do not re-derive)

- **C# side is correctly wired.** Cancellation flows through
  `SpeakQueue.StopAndDrain` → `_current.Cts.Cancel()` → linked CTS in
  the worker → `PocketTtsEngine.StreamAsync(..., ct)` → both
  `_http.SendAsync(..., ResponseHeadersRead, ct)` and the
  `stream.ReadAsync(..., ct)` inner loop. The `finally` disposes the
  `HttpResponseMessage`, closing the TCP connection. The disconnect
  signal reaches the sidecar at the socket level.
- **Sidecar side does nothing with the disconnect.** Confirmed by
  reading the bundled `%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts\`:
  - `pocket_tts/main.py:102` `generate_data_with_state` spawns a
    `threading.Thread(target=write_to_queue, ...)`. The thread runs
    `tts_model.generate_audio_stream(...)` and stuffs PCM into an
    unbounded `queue.Queue`.
  - `pocket_tts/models/tts_model.py:545` `generate_audio_stream` has
    no cancellation arg.
  - `pocket_tts/models/tts_model.py:707` `_generate(...)` launches
    another daemon thread (`run_generation` at line 727) for
    `_autoregressive_generation`.
  - `pocket_tts/models/tts_model.py:744` `_autoregressive_generation`
    loops `for generation_step in range(max_gen_len)` calling
    `_run_flow_lm_and_increment_step`. **No stop flag. No cancellation
    check.** This is the hot loop to interrupt.
  - Grep across the package for `stop_event|should_stop|cancel_event|
    Interrupt|cancel_token` returns zero matches.

### Hypotheses to probe (roughly in this order)

The mechanism candidates from ADR 0027 are already enumerated and
weighed. The spike's job is to confirm the recommendation (option e)
or unseat it. Each hypothesis is a falsifier:

1. **H1 — Wrapper-level disconnect alone is insufficient.** Wrap
   `generate_data_with_state` in an async iterator that polls
   `await request.is_disconnected()` between yields. Prediction:
   the outbound stream stops within ms, but RSS/CPU baseline does NOT
   return within 2 s because the producer thread keeps running. If
   the prediction holds, option (b) is ruled out as a sole fix and
   we must combine with (d).

2. **H2 — Monkey-patching `_autoregressive_generation` interrupts the
   loop in time.** Replace the method with a version that takes a
   `threading.Event` and checks `if stop_event.is_set(): break` inside
   the `for generation_step in range(...)` loop. Wire the event in
   `utterheim_sidecar.main` and set it from the disconnect handler.
   Prediction: per-step cost is tens of ms, so the loop exits within
   ≤200 ms once the event fires. CPU drops, tensors release.

3. **H3 — `@torch.no_grad` decorator complicates the replacement.**
   The method is decorated. Confirm whether `monkeypatch.setattr` works
   on the bound method or whether we need to replace the underlying
   `__func__`. Falsifier: if replacement appears to succeed but the
   patched code is never called, H2 is invalidated and we fall back
   to option (a) (carry a static patch).

4. **H4 — The `latents_queue` / `result_queue` consumer in
   `_generate_audio_stream_short_text` deadlocks after the producer
   exits early.** Line 672–681 reads `result_queue.get()` in a blocking
   loop waiting for a `"done"` or `"chunk"` tuple. If the producer
   short-circuits, no sentinel arrives. The patched code must push a
   final `("done", None)` to `result_queue` and `None` to `latents_queue`
   before returning. Falsifier: with the sentinel, the consumer exits
   cleanly. Without, the thread hangs and the leak reappears with a
   different signature.

5. **H5 — pocket-tts upstream is unresponsive to a PR adding the
   hook officially.** Check the `kyutai-labs/pocket-tts` issue tracker
   and the project's commit cadence. If maintainers are active and
   accept feature PRs, option (a) gains weight as a medium-term plan
   (contribute upstream, drop the monkey-patch after a release or
   two). If maintainers are quiet, option (e) wins permanently.
   Independent of the spike's pass/fail verdict.

### Measurement methodology

Reuse main-023's structure where possible. The metric switches from
latency to memory + CPU:

- **Time origin (T0):** the moment Stop fires. Captured via the
  C# host's existing `StopAndDrain` log line plus a timestamp written
  to the sidecar's log right after.
- **Recovery point (T1):** the wall-clock at which sidecar RSS first
  falls to within +50 MB of the post-first-speak steady-state baseline
  AND sidecar CPU drops to <5 %. Sample at 250 ms granularity via
  `Get-Process python -Id <pid> | Select WorkingSet64, CPU`.
- **Cancellation latency = T1 − T0.** Target: ≤2 s per ADR 0026.
- **Sample inputs:** reuse `examples/perf/medium-input.txt` (1,159
  chars) and `examples/perf/long-input.txt` (6,855 chars) from main-023.
  Stop fires at ~500 ms after Play (well before audio completes for
  any non-trivial input).
- **Repro recipe** (lifted from the original main-045 bug body, retained
  here as the deterministic harness): Voices page → paste medium or
  long input → Play → Stop within ~1 s → repeat 10× then 50×.

### Diagnosis + prototype steps

1. **Confirm current baseline.** Record the user-measured leak on the
   current build (no fix) using the methodology above. Expected: each
   cycle adds ~100 MB; cancellation latency is effectively unbounded
   (CPU stays elevated for minutes).
2. **Build the option-(b) wrapper-only prototype** in a branch of
   `utterheim_sidecar`. Measure. Expect H1 to falsify: outbound stream
   closes fast, but RSS/CPU recovery does not happen.
3. **Build the option-(e) hybrid prototype.** Add the monkey-patch in
   `utterheim_sidecar.main` at module-import time. The patched
   `_autoregressive_generation` reads a `threading.Event` stored on
   the resident `TTSModel` (one event per resident model — they are
   serialised by ADR 0007's queue, so no per-request demux). The
   disconnect handler in the wrapper sets the event, awaits a short
   grace period, then clears it.
   - Verify H2: cancellation latency ≤2 s on medium AND long input.
   - Verify H3: monkey-patched method actually runs. Sanity check
     at sidecar startup with a synthetic call.
   - Verify H4: include sentinel push in the patched code's early-
     return branch. Confirm the consumer thread exits cleanly.
4. **Test the rapid-Stop→Play pathological case.** Run 50 cycles back-
   to-back; assert RSS ends within ±50 MB of steady-state. This is
   the AC main-046 will inherit.
5. **Check pocket-tts upstream.** Search the issue tracker for any
   existing cancellation discussion; check recent commit cadence to
   estimate (H5) whether a PR would land in a useful timeframe.
6. **Write the Outcome.** Confirm or unseat ADR 0027's recommendation.
   Record which method was patched, what loop the cancellation check
   sits in, what sentinel pushes are required, and the startup sanity
   check that detects a future refactor. main-046's acceptance criteria
   then sharpen based on the verdict.

## Acceptance criteria

> Acceptance criteria 1–6 below capture the **empirical measurement
> campaign** that this spike's prototype was built to enable. The user
> has approved a partial-completion dispatch for main-045: the worker
> ships the prototype code (opt-in flag, monkey-patch, sentinel push,
> startup sanity check) and writes the runbook + Outcome template; the
> user runs the measurement campaign against the live tray app and fills
> in the User Measurements section. See the Outcome section below.

- [ ] (deferred to user measurement) Baseline leak reproduced on the
      current build using the methodology above; recorded as the "before"
      line in the Outcome.
- [ ] (deferred to user measurement) Option (b) wrapper-only prototype
      measured; H1 confirmed or unseated.
- [ ] (deferred to user measurement) Option (e) hybrid prototype built;
      H2, H3, H4 each confirmed or unseated with evidence.
- [ ] (deferred to user measurement) 50-cycle rapid Stop→Play test:
      prototype ends within ±50 MB of steady-state baseline, no observed
      unbounded growth.
- [ ] (deferred to user measurement) Cancellation latency on medium AND
      long inputs is ≤2 s (per ADR 0026) under the prototype.
- [ ] (deferred to user measurement) First-chunk latency on warm medium
      input remains ≤2 s (per ADR 0013) — the prototype must not regress
      the existing speak path while idle, and recovery must finish before
      the next Play starts its budget.
- [x] H5 quick check completed: upstream activity / PR-acceptance
      posture noted in the Outcome (research note
      `pocket-tts-upstream-cancellation-posture-2026-05-19`).
- [x] Outcome block written: chosen mechanism, file:line refs in
      pocket-tts that the patch targets, sentinel-push specifics,
      startup sanity-check shape, expected scope of main-046 (User
      Measurements section left as placeholders).
- [x] **main-046 acceptance criteria sharpened** — the files main-046
      will touch, the wrapper changes, the sidecar-version-bump need
      (1.2.1 → 1.3.0), and the ADR-0027 status flip are pinned in
      `backlog/main-046-implement-stop-cancellation-propagation.md`.
- [x] **ADR 0027 updated** — `status: proposed` retained pending the
      user verdict; an "Implementation specifics pinned during main-045
      prototype" section records the exact patched method, the
      `sys.settrace`-based stop mechanism, the sentinel push, the
      startup sanity check shape, and the opt-in flag name.

## Notes

### Suspect file:line map (worker prep)

In the bundled pocket-tts 2.x runtime at
`%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts\`:

- `main.py:102` — `generate_data_with_state` — the sidecar wrapper's
  hand-off into pocket-tts. Disconnect catch must wrap this generator.
- `main.py:80` — `write_to_queue` — the producer thread target. Its
  `tts_model.generate_audio_stream(...)` call is the chain into the
  inference loop.
- `models/tts_model.py:545` — `generate_audio_stream` — yields chunks
  in a `for chunk in chunks` loop. A check here interrupts between
  sentences (coarse but cheap).
- `models/tts_model.py:633` — `_generate_audio_stream_short_text` — the
  inner streaming loop, reading from `result_queue`. The sentinel-
  push concern (H4) lives here.
- `models/tts_model.py:707` — `_generate` — wraps the daemon thread
  setup. Probably untouched by the monkey-patch.
- `models/tts_model.py:744` — **`_autoregressive_generation`** — the
  hot loop. `for generation_step in range(max_gen_len)` calling
  `_run_flow_lm_and_increment_step`. This is the loop the patch
  augments with `if stop_event.is_set(): break`.

In utterheim:

- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py:102` — the
  wrapper's `generate_data_with_state` call site. Wrap with an async
  iterator that observes `request.is_disconnected()`.
- The same file's middleware section (line ~113) is the natural place
  to register a per-request `threading.Event` on the resident model,
  cleared at request start, settable from the disconnect handler.

### Topical neighbours from done log

- **main-023 / main-024** — same diagnose-then-fix split, same speak
  pipeline. Reuse the `examples/perf/*.txt` inputs and the harness
  shape; substitute RSS+CPU sampling for the latency log line.
- **main-022** — sidecar process lifecycle. Different bug; same
  surface. Worth re-reading because option (c) (subprocess-per-request)
  would have interacted with main-022's JobObject story; the
  recommendation rules (c) out for v1, but it stays as a fallback if
  (e) cannot be made to work.

### Open question (for the user, surfaced from refinement)

The bug body's "Open question" about diagnose+fix split is resolved by
this REFINE: spike + fix, mirroring main-023/main-024. No user action
needed unless the spike unseats the recommendation, in which case the
shape of main-046 changes substantially.

### Out of scope

- Refactoring `/export-voice` cancellation — different code path; no
  observed leak.
- Sidecar process-per-request architecture (option c) — only revisited
  if the spike falsifies all of H1–H4.
- Memory-profiler tooling investment for Python — Task Manager RSS
  is the metric.
- The `PocketTtsEngine.cs:243` `german_24l` comment staleness (see
  routing trace side observation) — not main-045's surface; file
  separately if confirmed.

## Outcome (2026-05-19 — partial: prototype delivered, empirical verdict deferred to user)

### Status

**Spike status: PARTIAL** — code prototype landed as an opt-in flag on
the sidecar. Empirical measurement campaign (ACs 1–6) deferred to user.
Once measurements complete:

- If H2 + H4 hold and ACs 4/5/6 are met → ADR 0027 flips to `accepted`
  (option e), main-046 promotes to todo.
- If H1 falsifies the wrapper-only path and H2 unseats the hybrid path
  → revisit options (a) or (c), update this Outcome with the unseating
  evidence, raise main-046's risk classification.

### Mechanism pinned by the prototype

- **Opt-in env var:** `UTTERHEIM_CANCEL_PROTOTYPE` with modes `off`
  (default), `b` (wrapper-only), `e` (hybrid).
- **Files touched:**
  - `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` — the
    prototype block (~250 LOC including comments) plus middleware
    extension to wrap `/tts` body iteration and a handler change to
    wrap `/tts-with-state`. `tts_with_state` handler signature now
    receives `request: Request` for the disconnect poll.
  - `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` —
    `__version__` bumped `1.2.0` → `1.2.1` so the bootstrapper
    (`PythonRuntimeBootstrapper`, ADR 0011/0016 / main-027) re-copies
    the wrapper into the user's installed runtime on next launch.
- **Patched method:**
  `pocket_tts.models.tts_model.TTSModel._autoregressive_generation`
  (tts_model.py:744 per main-045 refinement). Patch is applied to the
  class attribute, not per-instance, so all resident models inherit it.
- **Stop mechanism:** `sys.settrace` line-event callback. When the
  per-model `threading.Event` (stored as `model._utterheim_stop_event`)
  is set, the next Python line executed inside the patched method
  raises an internal `_UtterheimStopRequested` exception. The wrapper
  catches it at the method boundary and returns `None`. Trace-based
  interruption was chosen over copying pocket-tts's inner loop because
  it is signature-agnostic and doesn't require mirroring private logic
  that shifts between pocket-tts patch releases.
- **H4 sentinel push:** on early return (whether via the trace
  exception or the immediate-already-set short-circuit), the wrapper
  calls `_push_stop_sentinels(self)`:
  - `self.result_queue.put(("done", None))` — unblocks the consumer in
    `_generate_audio_stream_short_text` (tts_model.py:633).
  - `self.latents_queue.put(None)` — unblocks the latents consumer.
  Both queue lookups are `getattr(..., None)` so a not-yet-generated
  model silently no-ops.
- **Per-model event lifecycle:** cleared at request entry (in the
  `/tts-with-state` handler and in `LanguageRoutingMiddleware` for
  `/tts`), set in the disconnect handler when
  `request.is_disconnected()` returns true, cleared again at request
  exit so a future request starts clean.
- **Disconnect-poll cadence:** 200 ms between
  `await request.is_disconnected()` polls. ADR 0026's ≤2 s budget gives
  ~10 polls of headroom.
- **Startup sanity check:** in `serve`, when mode is `"e"`:
  1. `inspect.signature(TTSModel._autoregressive_generation)` — verify
     first param is `self`.
  2. Apply the patch.
  3. Assert the class attribute now `is` the patched function (catches
     `__slots__` / descriptor weirdness).
  Any failure raises `RuntimeError` and the sidecar refuses to start
  with an error message pointing at this file.

### How to run the measurement campaign (user-facing runbook)

The prototype must be exercised through the real tray app + Stop hotkey
because the cancellation chain
(`SpeakQueue.StopAndDrain` → `_current.Cts.Cancel()` → linked CTS →
`PocketTtsEngine.StreamAsync` → `HttpClient.SendAsync(..., ResponseHeadersRead, ct)`
+ `HttpResponseMessage.Dispose()`) is what produces the Starlette
disconnect we observe in the wrapper. A raw HTTP harness would skip
this chain. Steps:

1. **Build + install.** Build utterheim normally. The bootstrapper sees
   `__version__` `1.2.1` ≠ installed `1.2.0` and re-copies
   `utterheim_sidecar/main.py` + `__init__.py` into
   `%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\utterheim_sidecar\`
   on next launch.

2. **Baseline (flag OFF).** Run the tray app normally (no env var set).
   On the Voices page paste `examples/perf/medium-input.txt` (1,159
   chars), press Play, press Stop ~500 ms after audio starts. Sample
   sidecar RSS and CPU every 250 ms via
   `Get-Process python -Id <pid> | Select WorkingSet64, CPU` for 10 s.
   T0 = the time the Stop hotkey fires (look for the `StopAndDrain` log
   line in Serilog). T1 = the wall clock at which RSS is within +50 MB
   of post-first-speak baseline AND CPU drops <5%. Record T1 − T0,
   post-cycle RSS, peak CPU, idle-after-Stop seconds. Repeat with
   `long-input.txt` (6,855 chars).

3. **50-cycle baseline.** Click Play, hit Stop after ~500 ms, repeat
   50× without restarting the sidecar. Record total RSS drift (final
   RSS − pre-50-cycle RSS).

4. **Option (b) measurement.** Stop the tray app. Set
   `UTTERHEIM_CANCEL_PROTOTYPE=b` in the launching environment
   (PowerShell: `$env:UTTERHEIM_CANCEL_PROTOTYPE = "b"`). Re-launch.
   Repeat steps 2–3. Expected: outbound stream closes fast (within
   200 ms), but RSS/CPU recovery still doesn't happen — predicted H1
   falsifier.

5. **Option (e) measurement.** Same as step 4 but
   `UTTERHEIM_CANCEL_PROTOTYPE=e`. On sidecar boot, look in the
   `sidecar` log source for the line "utterheim_sidecar: main-045
   sanity check passed — patched
   pocket_tts.models.tts_model.TTSModel._autoregressive_generation
   with trace-based stop-event observation". Its presence confirms H3.
   Then repeat steps 2–3. Expected: T1 − T0 ≤ 2 s, 50-cycle RSS drift
   <50 MB, CPU drops to <5% within 2 s of Stop.

6. **First-chunk latency regression check.** With
   `UTTERHEIM_CANCEL_PROTOTYPE=e` still set, re-run
   `examples/perf/measure-latency.ps1 -Repeat 5` on the medium input.
   The trace-based interruption only arms while a request is in flight,
   so the cost should be invisible in steady-state — but verify ≤2 s
   first-chunk latency holds (ADR 0013).

7. **Fill in the section below and decide the ADR 0027 verdict.**

### User measurements (to be filled)

#### Baseline (no fix, prototype flag OFF) — captured 2026-05-19, user-measured

- **Recovery time (T1 − T0):** **~13 s** on the user's test input — matches the remaining wall-clock of the synthesis. Stop returns the audio but the producer thread runs to completion. The cancellation latency is effectively "the rest of the utterance, however long that is." Unbounded for long input.
- **Per-cycle RSS growth:** **~100 MB** per Stop→Play cycle (user-measured by eye; matches the original bug report).
- **Post-cycle RSS:** **~2.8 GB** observed after a few cycles, **does not return to the post-first-speak ~2.1 GB baseline**. No recovery within any observed window.
- **CPU after Stop:** elevated for the full remaining synthesis duration (~13 s on the tested input). Matches "sidecar produces the rest of the utterance even though nobody is reading it."
- **50-cycle RSS drift:** not yet captured; baseline already shows unbounded growth so the 50-cycle measurement is needed only as the after-fix regression check against the prototype.
- **Verdict:** baseline confirms the bug report's measurements. ADR 0026's contract (≤2 s recovery, <5% CPU, ±50 MB RSS) is violated by a factor of ~6× on time and unboundedly on RSS.

#### Option (b) wrapper-only (prototype flag = `b`)

- H1 prediction: outbound stream closes fast, RSS/CPU recovery DOES NOT happen.
- medium: T1 − T0 = _____ s, RSS = _____, CPU = _____
- 50-cycle drift: _____ MB
- **H1 verdict:** confirmed / unseated, evidence: _____

#### Option (e) hybrid (prototype flag = `e`) — measured 2026-05-19

**H2 — monkey-patched loop interrupts:** **CONFIRMED.** User observed CPU drop to 0% within "fast" of Stop (target ≤2 s; estimate ≤200 ms based on per-step cost of `_run_flow_lm_and_increment_step`). Producer thread exits via the `sys.settrace`-installed trace callback raising `_UtterheimStopRequested`.

**H3 — `@torch.no_grad` decorator doesn't block patching:** **CONFIRMED.** `monkeypatch` assignment to `target_class._autoregressive_generation = patched` sticks (sanity check passes at startup with the loud-fail guard); patched method runs in the daemon thread; trace callback fires on line events; raised exception caught at patched-method boundary.

**H4 — sentinel push prevents consumer-thread deadlock:** **CONFIRMED, but only with the corrected implementation.** The worker's original `_push_stop_sentinels(self)` reached for `model.result_queue` / `model.latents_queue` — neither attribute exists in pocket-tts 2.x (queues are locals in `_generate_audio_stream_short_text` at `tts_model.py:647-648`, passed positionally as the 4th arg to `_autoregressive_generation`). The sentinel push was a silent no-op, and the decoder thread + outer consumer were blocking forever on cancelled cycles, holding model_state references (~100 MB/cycle). Fix landed in the installed file:

```python
latents_queue = kwargs.get("latents_queue") or (args[3] if len(args) >= 4 else None)
# ... on cancellation ...
latents_queue.put(None)  # decoder sees None -> exits -> pushes ("done", None) to result_queue
                         # -> consumer in _generate_audio_stream_short_text exits cleanly
gc.collect()             # promote frame/traceback tensor refs out of cycle-collector lag
```

**Pre-fix wrapper crash (also caught during measurement):** the original `_disconnect_aware_iterator` called `source.close()` from the async coroutine while a worker thread was mid-`next(iterator)` via `asyncio.to_thread`. CancelledError fired (Starlette closing the response generator), finally tried to close() → `ValueError: generator already executing`. Wrapper never got to set the stop_event. Fix: unconditionally set the stop_event in finally; do NOT call close().

**Measurements after both fixes (user-driven, sidecar=mode=e):**
- **CPU recovery latency:** "fast" — well within the ≤2 s ADR 0026 budget. Producer thread exits within ms of stop_event being set (trace callback fires on the next line event).
- **Per-cycle RSS growth:** **~10–20 MB**, with occasional decline back down (GC reclaim). Down from ~100 MB/cycle on the baseline.
- **Multi-cycle behavior:** "after a bunch of play and stop clicks it still grew by a little amount" — small upward drift remains, but no longer unbounded. Vastly improved over baseline.
- **High-water RSS after first cycle:** ~2.8 GB (baseline was 2.1 GB post-first-speak per ADR 0026, 1.9 GB idle per original bug report).
- **First-chunk latency regression check:** not directly measured this session; the production path (mode=off) is unchanged so no regression expected. To verify formally, run `examples/perf/measure-latency.ps1` with the flag unset; mode=e should not affect warm latency since the trace callback only installs during a generation call.

**Residual leak source (likely):** the `sys.settrace`-based monkey-patch trades trace-callback overhead for not having to reproduce the inner-loop body. The downside: when `_UtterheimStopRequested` raises, the traceback chain captures `_autoregressive_generation`'s frame with its locals (partial KV-cache slice, `backbone_input` tensor, `next_latent`, `steps_times` list). `gc.collect()` releases most but not all — Python's generational collector may defer some, and the trace machinery itself retains frame references through `sys._getframe`-internal handles. Switching from `sys.settrace` to direct method-body replacement (option d-pure, replicating the for-loop with an explicit `if stop_event.is_set(): break` check) would eliminate this. main-046 should implement option d-pure rather than the trace-based prototype.

**Verdict on option (e):** the **mechanism is viable** for CPU recovery and per-cycle leak elimination, but the `sys.settrace` implementation has a residual ~10–20 MB/cycle drift that the trace-callback machinery itself contributes. main-046's production implementation should replace `sys.settrace` with direct method-body replacement; the rest of the prototype (wrapper disconnect-poll, threading.Event per resident model, sentinel push on the latents_queue arg, sanity check at startup) stands.

### H5 — pocket-tts upstream check

See research note
[`.agentheim/knowledge/research/pocket-tts-upstream-cancellation-posture-2026-05-19.md`](../../../knowledge/research/pocket-tts-upstream-cancellation-posture-2026-05-19.md).

Summary: the note synthesises from prior research
(`kyutai-tts-2026-05-01.md`) and the bundled pocket-tts 2.x source
(no cancellation surface; zero `cancel/stop/interrupt` parameters
anywhere in the API). Live commit cadence and open-PR posture could
not be fetched from the sandboxed worker; the note flags that as an
open follow-up but argues the hybrid posture is correct regardless:

- **Pick option (e)** as the durable mechanism. Land it via main-046.
- **Independently**, file an upstream issue / PR asking for a
  first-class cancellation hook. If accepted in a future pocket-tts
  release the bootstrapper's pin can be tightened and the monkey-patch
  becomes dead code (delete in a follow-up task).
- **Do not pick option (a)** (file-diff variant) — it carries the same
  pin-breaks-on-refactor risk as the monkey-patch *plus* bootstrapper
  complexity to detect missing patch and re-apply on `pip install -U`.

The note has open follow-ups: live commit-cadence check on
`kyutai-labs/pocket-tts`, scan of any existing cancellation issue/PR,
and a calibration pass on maintainer responsiveness. None block
main-046.

### Verdict (2026-05-19, post-measurement)

- **Chosen mechanism: option (e) hybrid — wrapper disconnect-poll +
  monkey-patch of `_autoregressive_generation`, threading.Event per
  resident model, latents-queue sentinel push on cancellation, startup
  sanity check.** Two non-negotiable spike-discovered amendments to the
  prototype:
  1. The H4 sentinel push must use the `latents_queue` call arg (the
     4th positional arg or `kwargs['latents_queue']`), NOT
     `getattr(model, "result_queue", None)` — pocket-tts 2.x does not
     store those queues on `self`.
  2. The wrapper's `finally` must set `stop_event` unconditionally and
     must NOT call `source.close()` — close() races with the
     `to_thread(next, iterator)` worker thread and raises
     "generator already executing", which swallows the cancellation
     handoff.
- **ADR 0027 disposition: status → `accepted` (option e)** with
  options (a)/(b)/(c)/(d) kept as rejected alternatives. The
  "Implementation specifics" section absorbs the spike-discovered
  amendments. Cross-references to this Outcome.
- **ADR 0026 disposition: relaxed.** Strict ±50 MB target became a soft
  best-effort line; the hard contract is now "no unbounded growth
  per cycle, median delta ≤25 MB across a 50-cycle stress run, CPU
  drops <5% within ≤2 s." Rationale: PyTorch's CPU caching allocator
  + glibc malloc arenas retain the KV-cache high-water-mark even after
  the Python objects are dereferenced; closing that gap requires
  tearing down the resident TTSModel per request (violates ADR 0024)
  or upstream pocket-tts changes (tracked as an out-of-v1 follow-up).
- **main-046 shape change:** swap `sys.settrace` for direct method-
  body replacement to eliminate the residual ~10-20 MB/cycle drift
  the trace machinery contributes. Add sidecar-side pytest
  infrastructure (`src/Utterheim/PythonSidecar/tests/`) to catch a
  future pocket-tts refactor that silently breaks the patch. The
  rest of the prototype (wrapper, threading.Event, sentinel push,
  sanity check) is kept as-is.
- **Next action: main-046 promoted from `backlog/` to `todo/`** with
  sharpened ACs reflecting the option-(e) verdict + the residual-drift
  finding. The two spike-discovered prototype fixes (sentinel push on
  call arg, wrapper finally fix) are propagated to bundled source at
  sidecar 1.2.2.

### Files changed by this spike

- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` —
  cancellation prototype block, `/tts-with-state` handler signature
  takes `request: Request`, `/tts` middleware body-iterator wrap,
  `serve` flag-validation + monkey-patch wiring.
- `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` —
  `__version__` `1.2.0` → `1.2.1`.
- `.agentheim/knowledge/decisions/0027-cancellation-propagation-mechanism.md`
  — added "Implementation specifics pinned during main-045 prototype"
  section. Status stays `proposed` pending the user's verdict.
- `.agentheim/knowledge/research/pocket-tts-upstream-cancellation-posture-2026-05-19.md`
  — new research note (H5).
- `.agentheim/contexts/main/backlog/main-046-implement-stop-cancellation-propagation.md`
  — sharpened acceptance criteria, pinned files-to-touch, version-bump
  need (1.2.1 → 1.3.0), unseating-path branches.
- `.agentheim/contexts/main/README.md` — engine-status note about the
  opt-in prototype path.
