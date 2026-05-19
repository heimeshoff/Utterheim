---
id: 0026
title: Stop cancels in-flight synthesis within ≤2 s (amends ADR 0004)
scope: global
status: accepted
date: 2026-05-19
supersedes: []
superseded_by: []
amends: [0004]
related_tasks: [main-045, main-046]
related_research: []
---

# ADR 0026: Stop cancels in-flight synthesis within ≤2 s

## Context

ADR 0004 ("Stop drains the queue by default") specified that on Stop the
C# host cancels in-flight generation, the audio output device flushes,
and pending queue items are discarded. It assumed cancellation propagation
to the sidecar was cheap.

main-045 (2026-05-19) observed that the assumption does not hold against
the current pocket-tts sidecar contract:

- The C# host *does* cancel cleanly. `SpeakQueue.StopAndDrain()` cancels
  `_current.Cts`; the worker's linked `CancellationTokenSource` flows
  through `PocketTtsEngine.StreamAsync` into `HttpClient.SendAsync` and
  `Stream.ReadAsync`. The HTTP response is disposed in the `finally`,
  which closes the TCP connection. The disconnect signal does reach the
  sidecar at the socket level.
- The sidecar does not observe the disconnect. Pocket-tts's streaming
  generator (`pocket_tts.main.generate_data_with_state` and the underlying
  `TTSModel.generate_audio_stream`) is a synchronous generator that runs
  on a Starlette threadpool worker. It spawns two internal worker threads
  (`run_generation` → `_autoregressive_generation`, and `_decode_audio_worker`)
  that produce latents and PCM frames into in-process `Queue` objects.
  None of these threads check for disconnection, and Python threads are
  not preemptable.
- Result: when the C# host hits Stop, the sidecar keeps generating the
  remainder of the utterance to completion (often tens of seconds to
  minutes for long inputs), holding all tensor allocations the whole
  time. Rapid Stop→Play stacks several still-running generators in the
  one resident sidecar process — measured at ~+100 MB RSS per cancel
  cycle with no observed ceiling, plus several minutes of post-Stop CPU.

The v1 vision treats Stop as the user saying "be quiet, I need to think."
A Stop that leaks memory linearly and keeps the CPU pegged for minutes
is not silence; it is a release-blocker.

## Decision

The Stop contract is extended with a hard cancellation-propagation
deadline. From the C# `StopAndDrain` call:

1. The currently-playing audio device flushes within the existing
   audio-engine deadline (unchanged from ADR 0004).
2. Pending queue items are discarded (unchanged from ADR 0004).
3. **The sidecar's in-flight synthesis must release its CPU and tensor
   allocations within ≤2 s of the Stop signal.** "Release" means:
   - Sidecar CPU drops to <5% within ≤2 s of the Stop signal. **(Hard.)**
   - Sidecar RSS does not grow unboundedly across Stop→Play cycles —
     per-cycle drift is bounded and small enough that an interactive
     session does not exhaust memory. **(Hard.)** Target: median
     per-cycle delta ≤25 MB across a 50-cycle stress run.
   - The strict "RSS returns to within ±50 MB of post-first-speak
     baseline" form is **a soft target**, not a release gate.
     main-045's measurement showed the high-water-mark of an
     interrupted KV-cache allocation persists in PyTorch's CPU pool
     and glibc's malloc arenas even after the Python objects are
     freed; closing that gap requires either tearing down the
     resident TTSModel per request (which fights ADR 0024's preload
     story and ADR 0002's warm-resident framing) or an upstream
     pocket-tts change to recycle the KV-cache buffer. v1 accepts the
     one-time high-water-mark cost in exchange for the
     warm-resident-model guarantee.

The 2 s budget mirrors the first-chunk latency budget (ADR 0013): the
two budgets together define what "responsive" means on the speak path,
in both directions.

The implementation strategy (upstream patch / wrapper-level check /
subprocess-per-request / monkey-patched stop flag) is **not** pinned by
this ADR. main-045's spike investigates and ADR 0027 records the chosen
mechanism once the spike concludes. This ADR is the contract; ADR 0027
is the mechanism.

## Consequences

### Positive

- Stop becomes honest. The user's mental model ("silence the room") is
  upheld at the system level, not just at the audio output device.
- Unattended-tray usage stops accruing leaked memory under normal
  interactive workflows. The vision's "runs unattended" success criterion
  is reachable.
- Pairs cleanly with ADR 0007's C#-side cancellation plumbing: the C#
  contract is already correct; this ADR makes the sidecar honour it.

### Negative

- The sidecar's pocket-tts integration is no longer pure conformist
  consumption. Some form of cancellation-aware shim is required because
  pocket-tts 2.x does not expose any cancellation hook
  (no `cancel`, `stop`, `interrupt`, `stop_event`, `should_stop` arg
  anywhere in `generate_audio`, `generate_audio_stream`, or
  `_autoregressive_generation`; confirmed by reading the bundled
  package in `%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts`).
  The mechanism choice in ADR 0027 will carry some cost.
- ADR 0015's "stay outside pocket_tts" stance has to bend a little. The
  wrapper will need to either monkey-patch a pocket-tts internal,
  intercept the inference loop, or move synthesis into a subprocess.
  Acceptable because the leak is a worse failure mode than the coupling.

### Neutral

- ADR 0004's drain semantics are preserved; this ADR adds a deadline to
  the cancellation half of the Stop signal.

## Alternatives considered

- **Accept the leak as a "known issue".** Rejected; measured growth
  is ~100 MB/sec on the user's repro and there is no observed ceiling.
  This is not "leak containment" the way OOM defenses are; the working
  set grows unboundedly under normal interactive use.
- **Tighten the budget to ≤500 ms.** Rejected for v1; the underlying
  pocket-tts inference step (`_run_flow_lm_and_increment_step`) takes
  tens to hundreds of ms per latent and we need at least one step of
  headroom for whatever mechanism polls the stop flag. 2 s matches the
  first-chunk budget and leaves margin.
- **Loosen the budget to "before next Play".** Rejected; the user's
  repro shows that several cancels can be issued *while the prior cancel
  is still in flight*. A budget tied to the next Play would accumulate
  arbitrarily.

## References

- ADR 0004 — Stop drains the queue by default (amended here).
- ADR 0007 — Speak queue Channel<T> + CancellationToken routing (the
  C#-side mechanism this contract relies on).
- ADR 0027 — Cancellation propagation mechanism (the partner ADR with
  the implementation choice; status will be proposed/accepted as the
  spike concludes).
- main-045 — Diagnose pocket-tts cancellation surface (spike).
- main-046 — Implement Stop cancellation propagation (fix).
