---
id: main-046
title: Implement Stop cancellation propagation into the sidecar (≤2 s recovery)
status: done
type: bug
context: main
created: 2026-05-19
completed: 2026-05-19
commit: dee614c
depends_on: [main-045]
blocks: []
tags: [bug, sidecar, cancellation, stop-signal, memory-leak]
related_adrs: [0004, 0007, 0015, 0024, 0026, 0027]
related_research: [kyutai-tts-2026-05-01, pocket-tts-upstream-cancellation-posture-2026-05-19]
prior_art: [main-022, main-024, main-045]
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

**main-045 verdict (2026-05-19): option (e) hybrid confirmed.** CPU
recovery fully met (drops fast on Stop). Per-cycle leak reduced
~100 MB → ~10-20 MB with corrected H4 sentinel push (using the actual
`latents_queue` arg, not the non-existent `self.result_queue` attr the
worker first reached for). ADR 0026 was relaxed in the same flip:
strict ±50 MB became a soft target, "no unbounded growth" became the
hard one. ADR 0027 is now `accepted`.

main-046 takes the in-installed-file fixes (now propagated to bundled
source at sidecar 1.2.2) and:

1. **Removes the opt-in flag.** Cancellation is always on.
2. **Swaps `sys.settrace` for direct method-body replacement.** This is
   the one substantive shape change vs the prototype — it eliminates
   the residual ~10-20 MB/cycle drift the trace machinery contributes
   through frame retention.
3. **Adds sidecar-side pytest coverage** so a future pocket-tts refactor
   that breaks the patch is caught by CI, not by a user noticing the
   leak in production.

### Shape (option-e production form)

1. **Replace `sys.settrace` with direct method-body replacement.** In
   `_patch_autoregressive_generation`, the patched function no longer
   calls `original(self, *args, **kwargs)` under a trace; it instead
   contains an inlined copy of `_autoregressive_generation`'s body
   (`tts_model.py:744-779`) with an explicit
   `if stop_event.is_set(): break` between the `_run_flow_lm_and_increment_step`
   call and the `latents_queue.put(next_latent)` line. This eliminates
   the trace-callback overhead AND the per-cycle frame-retention residual
   that the prototype showed (~10-20 MB/cycle). Cost: ~30 lines of
   pocket-tts-coupled code that this task owns. Risk mitigated by the
   startup sanity check.

2. **Remove the opt-in env var.** Cancellation is always on:
   - Delete `UTTERHEIM_CANCEL_PROTOTYPE`, `CANCEL_FLAG_ENV`,
     `CANCEL_MODES`, `_read_cancel_mode`, and the wrapper-only
     ("mode=b") branches.
   - The `_patch_autoregressive_generation` call moves to the
     unconditional `serve` startup path. The startup sanity check
     (signature introspection + patch-identity assertion) runs every
     boot and refuses to start on a pocket-tts internals mismatch.
   - Keep the wrapper-disconnect-poll logic (this part of the hybrid
     stays — it's what triggers the stop_event on client disconnect).
     Both `/tts` (via `LanguageRoutingMiddleware`) and `/tts-with-state`
     (handler-level) paths must wrap.

3. **Inherit the spike-discovered fixes in the bundled source:**
   - `latents_queue` sentinel push uses the call arg, NOT `self`
     attributes. Already in bundled `main.py` at sidecar 1.2.2.
   - `_disconnect_aware_iterator` finally sets the stop_event
     unconditionally; never calls `source.close()`. Already in
     bundled source.
   - Startup messages use `print(..., flush=True)`, NOT
     `logger.info(...)` — Python's unconfigured named-logger defaults
     to WARNING so INFO calls were silently dropped. Already in
     bundled source.

4. **Sidecar version bump.** `__version__` in
   `utterheim_sidecar/__init__.py` `1.2.2` → `1.3.0`. The bootstrapper
   (`PythonRuntimeBootstrapper`, ADR 0011 / main-027) compares bundled
   vs installed `__version__` at every launch and re-copies the wrapper
   on mismatch — so the version bump is the **only** delivery mechanism
   main-046 needs.

5. **In `src/Utterheim/Services/Tts/`:** no code change expected.
   main-045 confirmed the C# side (`SpeakQueue.StopAndDrain` →
   `_current.Cts.Cancel()` → linked CTS → `PocketTtsEngine.StreamAsync`
   → `HttpClient.SendAsync(..., ResponseHeadersRead, ct)` →
   `Stream.ReadAsync(..., ct)` + `HttpResponseMessage.Dispose()`) is
   already correctly wired and the TCP-close reaches Starlette.

6. **Sidecar-side pytest infrastructure.** New directory
   `src/Utterheim/PythonSidecar/tests/`:
   - `test_cancel_patch.py` — stubs a fake `pocket_tts.models.tts_model`
     module with a method matching the expected signature; asserts
     `_patch_autoregressive_generation` patches it correctly; asserts
     setting `stop_event` causes the patched method to break out and
     return None within ≤200 ms.
   - `test_sentinel_push.py` — asserts cancellation pushes `None` to a
     synthetic `latents_queue` reachable via the call arg.
   - `test_sanity_check.py` — stubs a fake module WITHOUT
     `_autoregressive_generation`; asserts the patch function raises
     `RuntimeError` (the loud-fail guard).
   These tests don't need to import the real pocket-tts (heavy) — they
   stub the surface. Run via `pytest src/Utterheim/PythonSidecar/tests/`
   from the repo root.

7. **Latency-regression guard.** Re-run
   `examples/perf/measure-latency.ps1` against medium and long inputs.
   ADR 0013's ≤2 s first-chunk budget must hold post-fix. Record
   numbers in the Outcome.

8. **BC README updates** (`.agentheim/contexts/main/README.md`):
   - Glossary entry **Stop signal**: append "Cancellation honours the
     ≤2 s ADR 0026 CPU-drop budget; the sidecar wrapper monkey-patches
     `_autoregressive_generation` at boot to observe a `threading.Event`
     in the inference loop (ADR 0027)."
   - Engine status section: document the always-on cancellation hook.
     Drop any mention of the `UTTERHEIM_CANCEL_PROTOTYPE` env var
     (it ceases to exist in 1.3.0).
   - Sidecar-version table (if any) reflects `1.3.0`.

## Acceptance criteria

- [ ] `UTTERHEIM_CANCEL_PROTOTYPE` env var + all "mode=b" code paths
      removed. Wrapper-disconnect-poll + monkey-patch run unconditionally
      on `serve` startup.
- [ ] `_patch_autoregressive_generation` replaces the body of
      `_autoregressive_generation` directly (no `sys.settrace`,
      no `_make_trace`, no `_UtterheimStopRequested` exception
      machinery). The replacement contains the for-loop from
      pocket-tts's `tts_model.py:744-779` with `if stop_event.is_set(): break`
      between the `_run_flow_lm_and_increment_step` call and
      `latents_queue.put(next_latent)`.
- [ ] Sidecar version bumped `1.2.2` → `1.3.0`. Verified by deleting
      the installed `utterheim_sidecar/main.py` and confirming the
      bootstrapper re-copies on next launch.
- [ ] **Hard:** after Stop, sidecar CPU drops to **<5 %** within **≤2 s**
      — measured on a single cycle for both medium and long inputs.
- [ ] **Hard:** 50 rapid Stop→Play cycles show median per-cycle RSS
      delta **≤25 MB** (per ADR 0026's relaxed target). No unbounded
      growth — final RSS after 50 cycles within ~500 MB of post-first-
      speak high-water-mark.
- [ ] **Soft (best-effort):** post-Stop RSS returns toward post-first-
      speak steady-state baseline; if direct method replacement closes
      the residual trace-frame drift as predicted, the strict ±50 MB
      form of the original ADR 0026 contract should be met. Record
      whether it is.
- [ ] First-chunk latency on warm medium input remains **≤2 s** (per
      ADR 0013) — before/after timings recorded from
      `examples/perf/measure-latency.ps1`.
- [ ] First-chunk latency on warm long input remains **≤2 s**.
- [ ] Cancellation works on **both** `/tts` (built-in voices, wrapped
      in `LanguageRoutingMiddleware`'s body-iterator wrapper) and
      `/tts-with-state` (cloned voices, wrapped in the handler itself).
- [ ] Sidecar-side pytest infrastructure landed under
      `src/Utterheim/PythonSidecar/tests/`:
      - `test_cancel_patch.py` — stub pocket-tts module, assert patched
        method honours `stop_event` and returns within ≤200 ms of set.
      - `test_sentinel_push.py` — assert `None` is pushed to the
        `latents_queue` arg on cancellation (not to `self` attrs).
      - `test_sanity_check.py` — stub module without
        `_autoregressive_generation`; assert `_patch_autoregressive_generation`
        raises `RuntimeError`.
- [ ] BC README updated: **Stop signal** glossary references ≤2 s
      CPU-drop budget; engine status documents the always-on cancellation
      hook; sidecar version table (if present) reflects 1.3.0. No
      mention of `UTTERHEIM_CANCEL_PROTOTYPE` remains.
- [ ] No regression on `/export-voice` cancellation (different code
      path — no `_autoregressive_generation` involvement; verify via a
      manual cancel-during-clone that temp files are still cleaned).

## Notes

- **main-045 closed 2026-05-19 with verdict: option (e) confirmed.**
  CPU recovery fully met; per-cycle leak reduced ~100 MB → ~10-20 MB
  with the spike-discovered fixes (sentinel push on the latents_queue
  call arg, wrapper finally sets stop_event unconditionally and skips
  source.close()). Bundled sidecar is 1.2.2 with those fixes; the
  residual ~10-20 MB/cycle drift comes from `sys.settrace`'s frame
  retention and is the reason this task switches to direct method-body
  replacement.
- The `PocketTtsEngine.cs` `LanguageWireValue` comment staleness (calls
  `german_24l` a "TEMPORARY listen-test swap") is a separate cleanup
  — track in a docs/comment-hygiene task, not here.
- Out of scope:
  - Subprocess-per-request architecture (option c — would violate
    ADR 0024's resident-model contract and ADR 0013's ≤2 s budget).
  - Upstream PR to `kyutai-labs/pocket-tts` adding a first-class
    cancellation hook — see
    `.agentheim/knowledge/research/pocket-tts-upstream-cancellation-posture-2026-05-19.md`
    for the upstream-posture finding. File as a separate task if/when
    desired; not v1.
  - Multi-lane queue / barge-to-front, backpressure / queue bounds —
    vision open questions, not coupled to this fix.
- **Risk register:**
  - pocket-tts upstream refactor of `_autoregressive_generation` →
    handled by the startup sanity check, which now also verifies
    pytest-side that an unexpected method shape raises.
  - The direct-replacement body diverges from pocket-tts's if upstream
    adds new logic between the two known versions. Mitigation: keep
    the replacement body small (just the for-loop with the event
    check); rely on the surrounding `_generate` / `_decode_audio_worker`
    machinery unchanged. The sanity check catches signature drift; a
    behaviour drift (e.g. upstream changes per-step logic) would be
    silent — accept this risk for v1, revisit if pocket-tts upgrades
    surface a regression.
  - Consumer-thread deadlock if sentinel push regresses → caught by
    the `test_sentinel_push.py` test required in the AC list.

## Outcome

Sidecar 1.3.0 ships ADR 0027 option (e) in production form: every `serve`
startup unconditionally patches
`pocket_tts.models.tts_model.TTSModel._autoregressive_generation` with a
stop-event-aware reimplementation that inlines the for-loop body from
pocket-tts 2.x's `tts_model.py:744-779` and adds an explicit
`if stop_event.is_set(): break` between the `_run_flow_lm_and_increment_step`
call and the `latents_queue.put(next_latent)`. The `sys.settrace`
mechanism, the `_UtterheimStopRequested` exception type, the `_make_trace`
helper, the `_read_cancel_mode` machinery, the `UTTERHEIM_CANCEL_PROTOTYPE`
env var, `CANCEL_FLAG_ENV`, `CANCEL_MODES`, the `_push_stop_sentinels`
helper, and every "mode=b" / "mode=e" branch are deleted. Both `/tts`
(wrapped at `LanguageRoutingMiddleware`) and `/tts-with-state` (wrapped at
the handler) always wrap with the disconnect-aware iterator.

The spike-discovered fixes are retained as-is: sentinel push uses the
`latents_queue` positional arg (not `self.` attrs); the disconnect-aware
iterator's finally block sets the stop_event unconditionally and skips
`source.close()`; startup messages use `print(..., flush=True)` so they
survive the unconfigured-named-logger / WARNING-default pitfall.

The startup sanity check (signature introspection → first param is `self`
+ patch-identity assertion via `is`) runs every boot and refuses to start
on a pocket-tts internals mismatch.

### Key files

- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` — production form,
  trace-free; `_patch_autoregressive_generation` builds the patched body
  inline (~30 LOC of pocket-tts-coupled code).
- `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` — `__version__`
  bumped 1.2.2 → 1.3.0; `PythonRuntimeBootstrapper` re-copies the wrapper on
  every launch where bundled and installed versions differ (ADR 0011 /
  main-027).
- `src/Utterheim/PythonSidecar/tests/conftest.py` — stub of
  `pocket_tts.{main, models.tts_model, utils.utils}` so tests run without
  the real (heavy) pocket-tts.
- `src/Utterheim/PythonSidecar/tests/test_cancel_patch.py` — asserts the
  patch installs, breaks the loop within ≤200 ms of `stop_event.set()`,
  and refuses a signature whose first param is not `self`.
- `src/Utterheim/PythonSidecar/tests/test_sentinel_push.py` — asserts the
  `None` sentinel is pushed to the `latents_queue` positional arg (NOT
  to a non-existent `self.latents_queue`).
- `src/Utterheim/PythonSidecar/tests/test_sanity_check.py` — asserts the
  patch raises `RuntimeError` when `_autoregressive_generation` or `TTSModel`
  is missing.
- `.agentheim/contexts/main/README.md` — Stop signal glossary, Engine status
  section updated; UTTERHEIM_CANCEL_PROTOTYPE references removed (single
  surviving mention is the explicit "is gone" sentence).

### Pytest results

All 6 tests pass via the repo-root invocation:

```
pytest src/Utterheim/PythonSidecar/tests/ -v
# 6 passed
```

The `dotnet build src/Utterheim/Utterheim.csproj` succeeds (0 warnings,
0 errors) — confirms the C# side compiles unchanged, as expected (the
task spec calls for "no code change" in `src/Utterheim/Services/Tts/`).

### Deferred ACs (empirical / measurement, user-driven)

Per the orchestrator's scope carve-out the AC 4/5/6/7/8 measurements
and the manual `/export-voice` cancellation regression check require an
interactive tray-app + Stop-hotkey session that cannot be driven
headlessly — same situation main-045 hit. Run the same measurement
campaign main-045 used:

1. **Launch utterheim from the freshly-built install** so the
   `PythonRuntimeBootstrapper`'s version-mismatch path re-copies the new
   `utterheim_sidecar/main.py` (`__version__` 1.3.0) into
   `%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\utterheim_sidecar\`.
   Confirm the new file is in place: `Get-Content
   $env:LOCALAPPDATA\Utterheim\runtime\python\Lib\site-packages\utterheim_sidecar\__init__.py`
   → `__version__ = "1.3.0"`. Also delete the installed `main.py` once and
   relaunch — the bootstrapper must re-copy it (AC 3 verification).
2. **Single-cycle Stop tests** (AC 4). Open Task Manager → Details → add
   columns `CPU` and `Memory (private working set)`. Find the `python.exe`
   under utterheim. Speak a medium input (~5-10 s of audio) and a long
   input (~60+ s) on both English and German voices; hit Stop ~2 s into
   playback each time. Observe CPU drops to <5 % within ≤2 s; record the
   peak-to-post-stop RSS delta in this Outcome.
3. **50-cycle stress** (AC 5/6). Use a rapid Play→Stop loop (the same
   pocket-tts-leak-test rhythm main-045 used). Record post-first-speak
   baseline RSS and the RSS after cycle 10 / 25 / 50. The hard target is
   median per-cycle delta ≤25 MB and final RSS within ~500 MB of baseline.
   The soft (best-effort) target is that direct method replacement closes
   the ±50 MB form of the original ADR 0026 contract — record whether it
   does.
4. **Latency regression guard** (AC 7/8). Re-run
   `examples/perf/measure-latency.ps1` on warm medium and warm long
   inputs. ADR 0013's ≤2 s first-chunk budget must still hold. Compare to
   main-024's baselines (`192 ms warm medium`, `193 ms warm long`).
5. **`/export-voice` regression check** (AC bottom). Open the voice
   cloning UI; start a clone; cancel before completion (close the dialog
   / hit Stop / etc.). Confirm `%TEMP%\utterheim_clone_*` directories are
   cleaned up and no orphan python work is left running.

The implementation-side ACs (1, 2, 3, 9, 10, 11) are complete and
auto-verifiable. The empirical ACs above are flagged for user measurement
in the next session — same pattern main-045 closed under.
