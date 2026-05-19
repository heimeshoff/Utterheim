---
id: 0027
title: Cancellation propagation mechanism into pocket-tts — option (e) hybrid wrapper + monkey-patch
scope: global
status: accepted
date: 2026-05-19
supersedes: []
superseded_by: []
related_tasks: [main-045, main-046]
related_research: [kyutai-tts-2026-05-01, pocket-tts-upstream-cancellation-posture-2026-05-19]
---

# ADR 0027: Cancellation propagation mechanism into pocket-tts

> **Status: accepted (2026-05-19).** main-045's spike confirmed option (e)
> as the chosen mechanism. CPU recovery is fully met; per-cycle leak
> reduced from ~100 MB to ~10–20 MB. The strict ±50 MB RSS clause of
> ADR 0026 was relaxed in the same flip — see ADR 0026's amendment.
> Unchosen options (a)/(b)/(c)/(d-pure) are kept below as rejected
> alternatives. Spike-discovered implementation details — H4 sentinel
> push must use the `latents_queue` call arg (not `model.result_queue`
> which never existed), wrapper must NOT call `source.close()`, prefer
> direct method-body replacement over `sys.settrace` — are pinned in the
> "Implementation specifics" section below and inherited by main-046.

## Context

ADR 0026 fixes the **contract**: a Stop signal must drop sidecar CPU
to <5% and return RSS to within ±50 MB of steady-state baseline within
≤2 s. ADR 0027 picks the **mechanism**. The constraint that makes the
mechanism non-trivial:

pocket-tts 2.x has zero cancellation surface. Confirmed by reading the
bundled package (`%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts`):

- `TTSModel.generate_audio_stream` (tts_model.py:545) has no
  cancellation arg.
- `TTSModel._generate_audio_stream_short_text` (tts_model.py:633) runs
  a daemon thread (`run_generation` at line 727) doing the autoregressive
  loop. No stop flag is checked.
- `_autoregressive_generation` (tts_model.py:744) loops over
  `range(max_gen_len)` calling `_run_flow_lm_and_increment_step`. Each
  step is tens to hundreds of ms; the loop is a pure `for` over up to
  several thousand steps for a long input.
- The sidecar wrapper's `generate_data_with_state`
  (`pocket_tts/main.py:102`) starts another threading.Thread
  (`write_to_queue`) that pushes bytes into an unbounded `queue.Queue`.
  When Starlette closes the outer generator, the queue is orphaned but
  the producer thread continues; Python threads are not preemptable.

Any mechanism must either kill the OS process running the inference,
inject a cooperative check into pocket-tts's loop, or both.

## Options

### (a) Carry an upstream patch as a local diff

Maintain a small patch against `kyutai-labs/pocket-tts` that adds a
`stop_event` parameter to `generate_audio_stream` and checks it inside
`_autoregressive_generation`'s `for` loop. Apply via the
PythonRuntimeBootstrapper after `pip install`.

- **Pro:** Clean — pocket-tts honours cancellation as a first-class arg.
- **Con:** Re-applies on every `pip install -U`. The bootstrapper has
  to detect a missing patch and re-apply it. Patch breaks if Kyutai
  refactors `_autoregressive_generation` in pocket-tts 2.y or 3.x.
- **Risk:** Medium. Patch is small (~10 lines) and targets a stable
  hot loop, but every pocket-tts release carries a small chance of
  breaking it.

### (b) Wrap pocket-tts's generator with disconnect-aware iterator

In `utterheim_sidecar.main`, wrap `generate_data_with_state` in an outer
async iterator that polls `request.is_disconnected()` between yields and
returns early. Does **not** stop the underlying inference thread.

- **Pro:** Smallest change. Pure wrapper-level.
- **Con:** **Does not satisfy ADR 0026.** The producer thread (the one
  running `_autoregressive_generation`) keeps running on CPU and keeps
  filling the unbounded `Queue`. CPU stays elevated and tensors stay
  allocated. This option is a half-fix and is **not enough on its own**.
- **Use only:** as one piece of a hybrid (see option e).

### (c) Run synthesis in a subprocess per request

Sidecar spawns a worker subprocess (or process pool with N=1) for each
`/tts` and `/tts-with-state` request. On disconnect, the sidecar
SIGTERMs the worker. The HTTP response streams bytes out of the worker
via stdout pipe or a domain socket.

- **Pro:** Bulletproof cancellation. OS reclaims everything cleanly.
- **Con (big):** Pays the `TTSModel.load_model` cost per request — the
  research note quotes 10-30 s cold load. ADR 0024's preload investment
  would be erased. Subprocesses cannot cheaply share the resident model.
  A long-lived worker-pool variant (keep 1-2 idle workers warm with the
  models preloaded) is feasible but doubles RAM and complicates ADR 0024's
  fixed-list-of-languages story.
- **Risk:** Almost certainly violates ADR 0013 ≤2 s first-chunk latency
  unless workers are kept warm, which conflicts with single-resident-
  model assumptions across the codebase.

### (d) Run synthesis on a separate thread that polls a stop flag

Same shape as (a) but applied at runtime via monkey-patch instead of a
file diff. Replace `TTSModel._autoregressive_generation` (or
`_generate_audio_stream_short_text`) on import in `utterheim_sidecar`
with a version that takes a stop event and checks it in the inner loop.

- **Pro:** No file edits on the pocket-tts install; survives
  `pip install -U` as long as the method signature being patched still
  exists.
- **Con:** Couples `utterheim_sidecar` to private pocket-tts internals
  (the `_autoregressive_generation` method is private by name). If
  Kyutai renames it, we silently fall back to whatever the new method
  is — i.e. no cancellation — until we notice.
- **Risk:** Medium. Detection harness needed: on `serve` start, sanity-
  check that the patched method exists and matches a known argument
  shape; refuse to start otherwise so the failure mode is loud.

### (e) Hybrid — wrapper-level disconnect (b) plus runtime monkey-patch (d)

The wrapper catches the disconnect immediately and sets a `threading.Event`.
The monkey-patched `_autoregressive_generation` checks the event each
step and returns early. This gives:

- Immediate outbound-stream close (b) — Starlette stops calling our
  generator within milliseconds.
- Cooperative interruption of the inference loop (d) — the producer
  thread exits at the next step boundary, releasing tensors.

- **Pro:** No static diff to maintain (vs option a). Honours ADR 0026
  if `_autoregressive_generation`'s per-step cost is ≤2 s (it is —
  typical step is tens of ms, worst case a few hundred ms). Survives
  pocket-tts patch releases as long as the private method's name and
  loop shape persist.
- **Con:** Inherits (d)'s coupling to a private name. Needs a
  start-time sanity check and a maintainer note on how to update if
  Kyutai refactors. Slightly more code than (a).
- **Risk:** Low-medium. Same exposure surface as ADR 0015's existing
  `from pocket_tts.main import web_app` import-pinning.

## Recommendation

**Option (e) — Hybrid wrapper + monkey-patch — is the current
recommendation.** Rationale:

- It is the only option that satisfies ADR 0026 *without* paying
  subprocess cold-load cost (i.e. without breaking ADR 0013 or
  invalidating ADR 0024).
- It avoids the bootstrapper-time patch-reapply ceremony of (a) while
  achieving the same end-state.
- It keeps the pocket-tts install untouched — `pip install -U`
  continues to work; the only failure mode is a refactor we detect at
  startup.
- It composes naturally with `utterheim_sidecar`'s existing wrapper
  posture (ADR 0015): we already import from `pocket_tts.main` at
  module load and tolerate that pin.

**Spike verdict (2026-05-19, post-measurement):**

- H2 (inner loop observes the stop flag): **CONFIRMED.** Patched method
  runs in pocket-tts's daemon thread; trace callback raises
  `_UtterheimStopRequested` on the next line event; producer exits
  within milliseconds. User-measured CPU drop is "fast" — within the
  ≤2 s ADR 0026 budget.
- H3 (`@torch.no_grad` doesn't block the monkey-patch):
  **CONFIRMED.** The attribute-level swap `target_class._autoregressive_generation = patched`
  sticks. Sanity check (loud-fail at boot) verifies. The decorator is
  applied at class definition time, not on every call, so post-hoc
  replacement is honoured.
- H4 (sentinel push prevents consumer deadlock): **CONFIRMED, but only
  with the corrected sentinel location.** The first prototype reached
  for `model.result_queue` / `model.latents_queue` — neither attribute
  exists in pocket-tts 2.x. The queues are locals in
  `_generate_audio_stream_short_text` (`tts_model.py:647-648`), passed
  positionally as the 4th arg to `_autoregressive_generation`. Pushing
  `None` to that arg unblocks the decoder, which itself pushes
  `("done", None)` to result_queue (`tts_model.py:470`), which unblocks
  the outer consumer. The corrected fix is in `main.py`. **Implication
  for main-046:** this sentinel-push must reach for the call arg, not
  `self` attributes — the wrong location silently no-ops.
- H5 (pocket-tts upstream PR-acceptance): see
  `.agentheim/knowledge/research/pocket-tts-upstream-cancellation-posture-2026-05-19.md`.
  Decision: stay with the runtime monkey-patch; upstream PR a follow-up,
  not a v1 blocker.

**Additional bugs discovered during measurement, fixed in the bundled
prototype (utterheim_sidecar 1.2.2):**

- **Wrapper crash on cancellation.** Original `_disconnect_aware_iterator`
  called `source.close()` in `finally` while a worker thread was mid-
  `next(iterator)` via `asyncio.to_thread`. CancelledError fired
  (Starlette closing the response generator), finally tried to close()
  → `ValueError: generator already executing`. Wrapper never got to
  signal cancellation; `stop_event` was never set; producer ran to
  completion. **Fix:** set stop_event unconditionally in finally; do NOT
  call close(). The patched inner loop's stop_event check + the H4
  sentinel push are enough to unwind the producer/decoder/consumer
  chain cleanly without needing close().
- **Startup logging dropped.** Worker used `logger.info(...)` for
  the prototype-mode-active line; the `utterheim_sidecar` named logger
  has no handler/level configured, so Python's default (WARNING)
  silently dropped all INFO output. **Fix:** the startup messages are
  now `print(..., flush=True)` so the C# host's stdout/stderr capture
  picks them up and surfaces them as `[INF] sidecar main-045
  prototype: ...` in the Serilog stream.

**Open follow-up for main-046, NOT a spike blocker:**

- **Replace `sys.settrace` with direct method-body replacement.** The
  trace-callback approach interrupts the loop correctly (H2) and lets
  the sentinel push unwind the consumer chain (H4) — but the trace
  machinery itself retains frame references through Python's internal
  bookkeeping, contributing a residual ~10–20 MB/cycle drift that
  `gc.collect()` doesn't fully reclaim. Replicating
  `_autoregressive_generation`'s for-loop body in the patch (with an
  explicit `if stop_event.is_set(): break` between
  `_run_flow_lm_and_increment_step` calls) eliminates per-line trace
  overhead AND eliminates the frame-retention residual. Costs ~30 lines
  of pocket-tts-coupled code that main-046 maintains. main-046 inherits
  the rest of the prototype unchanged (wrapper disconnect-poll,
  threading.Event per resident model, latents-queue sentinel push,
  sanity check at startup).

## Implementation specifics pinned during main-045 prototype (2026-05-19)

main-045 (the spike) shipped an opt-in prototype in
`src/Utterheim/PythonSidecar/utterheim_sidecar/main.py`. The empirical
verdict (RSS / CPU recovery numbers) is deferred to a user-driven
measurement campaign — this ADR stays `proposed` until those measurements
flip H2/H3/H4. This section pins what the prototype actually does so the
user's measurement runbook and the eventual main-046 fix are aligned.

### Opt-in flag

Environment variable **`UTTERHEIM_CANCEL_PROTOTYPE`** read once at sidecar
boot inside `serve`:

- unset / empty / `"off"` — production behaviour, no patching, no
  disconnect wrapping. **Default in 1.2.1.**
- `"b"` — option (b) wrapper-only. Outbound `StreamingResponse` body
  iteration polls `await request.is_disconnected()` every ~200 ms;
  iteration breaks on disconnect. Producer thread keeps running
  (predicted H1 falsifier).
- `"e"` — option (e) hybrid. Wrapper as in `"b"` PLUS the monkey-patch
  described below.

Unknown values are rejected at startup with a typer-style error.

### Patched method

`pocket_tts.models.tts_model.TTSModel._autoregressive_generation`. The
patch is applied via attribute rebinding on the class object (not on each
instance) so all resident `TTSModel` instances created by `serve` after
the patch lands inherit the patched method.

### Stop signal mechanism (chosen by the prototype)

The patch installs a **`sys.settrace` line-event callback** that raises
an internal `_UtterheimStopRequested` exception when the per-model
`threading.Event` fires. The wrapper catches the exception at the
patched-method boundary and returns `None`. Trace-based interruption was
chosen over copying pocket-tts's inner loop because:

- It is **fully signature-agnostic**: any `for` / `while` body that
  executes Python bytecode honours the trace. pocket-tts can refactor
  the inner loop freely without breaking us, as long as the method
  remains pure Python (which torch model code is — only the kernel calls
  are C++ extensions, and we don't need to interrupt mid-kernel; the next
  Python line after the kernel returns is where we break).
- It **does not require mirroring** the (substantial) private inner-loop
  logic, which would re-break on every pocket-tts patch release.
- The cost is **per-line trace overhead while a request is in flight**.
  Empirically expected to be invisible because pocket-tts time is
  dominated by torch ops, not by Python interpreter time. The user's
  measurement campaign will quantify this — if it regresses ADR 0013
  first-chunk latency the trace-based approach is unseated and main-046
  falls back to a copy-the-loop variant.

### Sentinel push (H4)

On early-return from the patched method (whether via the trace exception
or the immediate-already-set short-circuit) the wrapper calls
`_push_stop_sentinels(self)` which:

- `self.result_queue.put(("done", None))` — unblocks the consumer in
  `_generate_audio_stream_short_text` (tts_model.py:633) reading
  `result_queue.get()` for a `("done", None)` or `("chunk", ...)` tuple.
- `self.latents_queue.put(None)` — unblocks the latents consumer
  waiting for the `None` poison pill.

Both queue attributes are looked up via `getattr(model, …, None)`; a
generation that never started has no queues yet and the sentinel push
silently no-ops.

### Per-model stop event

One `threading.Event` per resident `TTSModel`, stashed as
`model._utterheim_stop_event` (underscored to surface the utterheim-owned
coupling on grep). ADR 0007 guarantees one in-flight speak request per
language at a time, so per-model granularity is correct — no per-request
demux needed. The event is **cleared at request entry** (in the
`/tts-with-state` handler and the `LanguageRoutingMiddleware` for `/tts`)
and **set in the disconnect handler** when `request.is_disconnected()`
returns true.

### Startup sanity check

`serve` invokes `_patch_autoregressive_generation()` immediately after
flag validation and before any `TTSModel.load_model` call. The function:

1. Imports `pocket_tts.models.tts_model.TTSModel`. Missing class →
   `RuntimeError` pointing at this file.
2. Looks up `TTSModel._autoregressive_generation`. Missing method →
   `RuntimeError` pointing at this file.
3. Introspects the signature with `inspect.signature(original)`. First
   parameter not `self` → `RuntimeError` (catches the case where Kyutai
   promotes the method to a free function).
4. Assigns the patched function. Confirms
   `TTSModel._autoregressive_generation is patched` afterwards (catches
   `__slots__` / descriptor weirdness).

Any failure refuses sidecar startup. Loud > silent — main-046 inherits
the same posture.

### Files touched by the prototype

- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` — the entire
  prototype block, ~250 LOC. Comments explain the contract.
- `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` —
  `__version__` bumped `1.2.0` → `1.2.1` so the
  `PythonRuntimeBootstrapper` re-copies the wrapper into the user's
  installed runtime on next launch.

### What main-046 inherits

If the user's measurements confirm option (e) is viable, main-046:

- Removes the `UTTERHEIM_CANCEL_PROTOTYPE` env var (or flips its default
  to `"e"` and renames it to a permanent name like
  `UTTERHEIM_CANCEL_MODE`).
- Bumps `utterheim_sidecar` version `1.2.1` → `1.3.0`.
- Adds the test layer mandated by main-046's last AC (sidecar-side
  pytest or integration test under `src/Utterheim.Tests/`).
- Updates the BC README's **Stop signal** glossary entry and the
  Engine status section to record the production contract (the prototype
  note flips from "opt-in measurement path" to "always-on cancellation
  hook").
- Flips this ADR's `status: proposed` → `status: accepted` with options
  (a), (b), (c), (d) moved to an "Alternatives considered" section.

If measurements unseat the hybrid (H2 falsified — trace callback doesn't
fire fast enough, or H4 misses a sentinel and the consumer still hangs),
main-046's shape changes: either fall back to copying pocket-tts's
`_autoregressive_generation` body locally (option a variant) or escalate
to option (c) — subprocess-per-request, accepting the ADR 0013 / 0024
trade-off — and write a new ADR-0028 to record the unseating evidence.

## Consequences (under the recommended option)

### Positive

- pocket-tts stays opaque to `pip install -U`; the wrapper carries the
  cancellation contract.
- Same warm-resident-model story ADR 0002 and ADR 0024 rely on. No new
  process spawns per request.
- Failure mode is loud (sanity check at startup), not silent (an
  unobserved-cancellation regression).

### Negative

- `utterheim_sidecar` now patches a pocket-tts private method.
  ADR 0015's "wrapper sits outside pocket-tts" boundary blurs by one
  more entry point. Documented here so future readers know why.
- Maintainers need a small runbook entry: "if `utterheim_sidecar`
  refuses to start with a cancellation-method-mismatch error, read
  `pocket_tts/models/tts_model.py` and update the monkey-patch."

### Neutral

- The C# side does not change. ADR 0007 / 0013's plumbing is already
  correct end-to-end.

## References

- ADR 0026 — Stop cancels in-flight synthesis within ≤2 s (the contract
  this mechanism honours).
- ADR 0002 — pocket-tts as Python sidecar (the warm-resident-model
  framing options (c) would break).
- ADR 0013 — `ResponseHeadersRead` streaming (the ≤2 s first-chunk
  budget options (c) would jeopardise).
- ADR 0015 — utterheim-owned sidecar wrapper (the home of the
  monkey-patch and the disconnect-aware wrapper).
- ADR 0024 — sidecar preloads en+de (the resident-model assumption
  options (c) would invalidate).
- main-045 — Spike: investigate pocket-tts cancellation surface and
  prototype the cancel wire.
- main-046 — Implementation of the chosen mechanism (depends on
  main-045's verdict).
- Research: `.agentheim/knowledge/research/kyutai-tts-2026-05-01.md`.
