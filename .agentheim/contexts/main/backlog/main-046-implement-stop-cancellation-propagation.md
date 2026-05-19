---
id: main-046
title: Implement Stop cancellation propagation into the sidecar (≤2 s recovery)
status: backlog
type: bug
context: main
created: 2026-05-19
completed:
commit:
depends_on: [main-045]
blocks: []
tags: [bug, sidecar, cancellation, stop-signal, memory-leak]
related_adrs: [0004, 0007, 0015, 0024, 0026, 0027]
related_research: [kyutai-tts-2026-05-01]
prior_art: [main-022, main-024]
---

## Why

main-045 diagnoses pocket-tts's cancellation surface and prototypes
the cancel wire. This task lands the production fix that honours the
ADR 0026 contract: Stop must drop sidecar CPU <5 % and return RSS to
within ±50 MB of steady-state within ≤2 s.

The vision treats Stop as "be quiet, I need to think." Today's leak
(~+100 MB per cancelled cycle, CPU elevated for minutes) breaks that
on the natural Voices-page iteration workflow.

## What

The implementation shape is **inherited from main-045's prototype** (see
`utterheim_sidecar/main.py`'s "main-045 prototype" block + ADR 0027's
"Implementation specifics pinned during main-045 prototype" section).
main-045 shipped the cancellation path as an opt-in
`UTTERHEIM_CANCEL_PROTOTYPE` env var (modes `off` / `b` / `e`). The user's
measurement campaign verdict — captured in the main-045 Outcome — picks
between two main-046 shapes:

### If verdict confirms option (e) (the expected path)

1. **Promote the prototype to production default in
   `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py`:**
   - Rename the env var from `UTTERHEIM_CANCEL_PROTOTYPE` to a permanent
     name (recommendation: **`UTTERHEIM_CANCEL_MODE`**, defaulting to
     `"e"`). The `"off"` mode is preserved as an escape hatch for a
     pocket-tts release where the trace approach unexpectedly regresses
     ADR 0013 first-chunk latency.
   - Remove the `"b"` mode (it exists only to falsify H1 in main-045 —
     production has no use for "stream closes, producer leaks").
   - Promote the `_patch_autoregressive_generation` call out of the
     opt-in branch into the unconditional `serve` startup path. The
     startup sanity check (signature introspection + patch-identity
     assertion) runs every boot.
   - Keep the `LanguageRoutingMiddleware` `/tts` body wrapper and the
     `/tts-with-state` disconnect wrapper exactly as prototyped — they
     are the disconnect→stop-event signal path.
   - Verify the prototype's `sys.settrace` cost in production: if the
     measurement campaign shows it costs ≤5 ms of first-chunk-latency
     headroom (well within ADR 0013's ≤2 s budget) the trace approach
     ships as-is. If it costs more, **fall back to a "copy the inner
     loop locally" variant** (option a) — patched method becomes a full
     replacement of `_autoregressive_generation` with an explicit
     `if stop_event.is_set(): break` inside `for generation_step in
     range(max_gen_len)`. See ADR 0027's "Alternatives considered"
     after the flip.

2. **Sidecar version bump.** `__version__` in
   `utterheim_sidecar/__init__.py` `1.2.1` → `1.3.0`. The bootstrapper
   (`PythonRuntimeBootstrapper`, ADR 0011/0016 / main-027) compares
   bundled vs installed `__version__` at every launch and re-copies the
   wrapper on mismatch — so the version bump is the **only** delivery
   mechanism main-046 needs.

3. **In `src/Utterheim/Services/Tts/`:** no code change expected.
   main-045 confirms the C# side (`SpeakQueue.StopAndDrain` →
   `_current.Cts.Cancel()` → linked CTS → `PocketTtsEngine.StreamAsync`
   → `HttpClient.SendAsync(..., ResponseHeadersRead, ct)` →
   `Stream.ReadAsync(..., ct)` + `HttpResponseMessage.Dispose()`) is
   already correctly wired and the TCP-close reaches Starlette.

4. **ADR housekeeping:**
   - Flip ADR 0027's status `proposed` → `accepted`. Move options (a),
     (b), (c), (d) to an "Alternatives considered" section under the
     promoted option (e).
   - Pin the "Implementation specifics" section from a flag-prototype
     description into the canonical mechanism description (drop the
     "main-045 prototype" framing, keep the substantive facts).
   - ADR 0026 already cross-references ADR 0027 — no further edits needed.

5. **BC README updates** (`.agentheim/contexts/main/README.md`):
   - Glossary entry **Stop signal**: append "Cancellation honours the
     ≤2 s ADR 0026 budget; the sidecar wrapper owns the propagation
     mechanism (ADR 0027)."
   - Engine status section: update the "main-045 cancellation prototype
     is opt-in via `UTTERHEIM_CANCEL_PROTOTYPE`" note to "cancellation
     is always on (ADR 0027); the wrapper monkey-patches
     `_autoregressive_generation` at sidecar boot." Delete the
     prototype/opt-in language.
   - Sidecar-version table (if any) reflects `1.3.0`.

6. **Latency-regression guard.** Re-run `examples/perf/measure-
   latency.ps1` against medium and long inputs. ADR 0013 ≤2 s
   first-chunk budget must hold post-fix. Record numbers in the Outcome.

### If verdict unseats option (e)

Two sub-paths:

- **H2 falsified (trace approach too slow / doesn't fire):** swap the
  patched method body to a full copy-the-loop variant (option a). Same
  files touched, same version bump, same BC README updates. ADR 0027's
  status still flips to `accepted` but the "Implementation specifics"
  section gets a follow-up amendment recording the actual mechanism
  shipped.
- **All of H2/H3/H4 falsified (the hybrid doesn't work at all):**
  ADR 0027 flips to `status: rejected`, a new ADR-0028 documents the
  chosen alternative (likely option c — subprocess per request — paying
  the ADR 0013 / 0024 tax). main-046's shape changes substantially;
  re-refine before starting.

## Acceptance criteria

These ACs assume the option-(e) verdict path (the expected one). The
unseating paths invalidate this list — re-refine first.

- [ ] `UTTERHEIM_CANCEL_PROTOTYPE` env var renamed to permanent
      `UTTERHEIM_CANCEL_MODE`, default `"e"`, `"off"` preserved as
      escape hatch, `"b"` removed. Code comments updated to reflect
      production-on status.
- [ ] `_patch_autoregressive_generation` runs unconditionally on
      sidecar boot (not gated by env var); the env var only chooses
      between `"e"` (default) and `"off"` (no patch, no wrapper).
- [ ] Sidecar version bumped `1.2.1` → `1.3.0` in
      `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py`. The
      bootstrapper (main-027) re-copies the wrapper on next launch
      with no manual intervention — verified by deleting the installed
      `utterheim_sidecar/main.py` and confirming the launch path heals.
- [ ] After Stop, sidecar RSS returns to within ±50 MB of the
      post-first-speak steady-state baseline (~2.1 GB with en+de
      preloaded per ADR 0024) within **≤2 s** — measured on a single
      cycle for both medium and long inputs.
- [ ] After Stop, sidecar CPU drops to **<5 %** within **≤2 s**.
- [ ] 50 rapid Stop→Play cycles end with RSS within ±50 MB of
      steady-state (no observed unbounded growth).
- [ ] First-chunk latency on warm medium input remains **≤2 s** (per
      ADR 0013) — before/after timings recorded from
      `examples/perf/measure-latency.ps1`.
- [ ] First-chunk latency on warm long input remains **≤2 s**.
- [ ] Cancellation works on **both** `/tts` (built-in voices, wrapped
      in `LanguageRoutingMiddleware`'s body-iterator wrapper) and
      `/tts-with-state` (cloned voices, wrapped in the handler itself).
- [ ] Startup sanity check fires when the patched method is missing
      or has a mismatched signature — verified by a sidecar-side pytest
      that imports a fake `pocket_tts.models.tts_model` module without
      `_autoregressive_generation` and asserts
      `_patch_autoregressive_generation()` raises `RuntimeError`.
- [ ] A sidecar-side pytest under
      `src/Utterheim/PythonSidecar/tests/` (new directory) asserts:
      (a) the patched method observes the stop event;
      (b) the sentinel push lands in `result_queue` and `latents_queue`;
      (c) the consumer-thread analogue (a synthetic `queue.Queue` reader)
      unblocks within ≤200 ms of the event firing.
- [ ] ADR 0027 flipped to `status: accepted` with options (a)/(b)/(c)/(d)
      moved to "Alternatives considered". The "Implementation specifics
      pinned during main-045 prototype" section is rewritten without the
      prototype framing and serves as the canonical mechanism description.
- [ ] BC README updated: **Stop signal** glossary entry references the
      ≤2 s budget (ADR 0026). Engine status section drops the
      "opt-in prototype" note and documents the always-on cancellation
      hook (ADR 0027). Sidecar version table (if present) reflects 1.3.0.
- [ ] No regression on `/export-voice` cancellation (it uses a different
      code path — no `_autoregressive_generation` involvement; verify
      via a manual cancel-during-clone that temp files are still cleaned).
- [ ] `examples/perf/measure-latency.ps1` re-run on a clean runtime with
      the new wrapper; numbers logged in the Outcome.

## Notes

- **main-045 closed 2026-05-19 with PARTIAL status.** Prototype code
  landed as an opt-in flag (`UTTERHEIM_CANCEL_PROTOTYPE=b|e`) in
  `utterheim_sidecar/main.py` and sidecar version bumped 1.2.0 → 1.2.1.
  The empirical measurement campaign (RSS/CPU recovery numbers) was
  deferred to a user-driven pass. **Do not start this task until the
  user has filled in the main-045 Outcome's "User measurements" section
  and recorded a verdict.** The ACs above assume the option-(e) verdict;
  if measurements unseat the hybrid path, re-refine main-046 before
  starting work.
- The `PocketTtsEngine.cs` `LanguageWireValue` comment staleness (calls
  `german_24l` a "TEMPORARY listen-test swap") is a separate cleanup
  — track in a docs/comment-hygiene task, not here.
- Out of scope:
  - Subprocess-per-request architecture (option c — would violate
    ADR 0024's resident-model contract and ADR 0013's ≤2 s budget).
  - Multi-lane queue / barge-to-front (vision open question; not
    coupled to this fix).
  - Backpressure or queue bounds (vision open question; not coupled
    to this fix).
- **Risk register:**
  - pocket-tts upstream refactor of `_autoregressive_generation` →
    handled by the startup sanity check.
  - `@torch.no_grad` complicating method replacement → main-045
    falsifies before this task starts.
  - Consumer-thread deadlock if sentinel push is missed → main-045's
    H4 verifies; AC requires the test layer to catch a regression.
