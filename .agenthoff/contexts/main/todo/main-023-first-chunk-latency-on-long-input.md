---
id: main-023
title: Diagnose first-chunk latency on long input (~9s for 200-word paragraph)
status: todo
type: spike
context: main
created: 2026-05-03
completed:
commit:
depends_on: [main-011, main-021]
blocks: [main-024]
tags: [spike, performance, sidecar, streaming]
---

## Why

The vision sets a ≤2 s first-chunk-latency budget (vision §"What success
looks like" criterion 1, plus the seed glossary entry for **First-chunk
latency**: "Target ≤2 s; pocket-tts claims ~200 ms").

During main-018 first-run verification:
- Short sentence ("Hello, this is mockingbird.") → ~1 s to audible. Within budget.
- ~200-word paragraph → **~9 s to audible**. ~4.5× over budget.

Streaming itself is working — playback starts before synthesis completes,
satisfying main-018's *correctness* criterion 3. But the vision-level
*latency* target is missed badly enough that the read-aloud experience
will feel sluggish in the v1 scenario it's designed for.

The fix can't be designed without knowing what's adding the ~7 s. This
spike finds the bottleneck and writes the diagnosis. The follow-up
**main-024** lands the actual fix.

## What

A diagnosis-only investigation. **No production code changes.** The
deliverable is timing data + code references + a written verdict that
tells main-024 exactly what to change.

### Hypotheses to probe (roughly in this order)

1. **Whole-text preprocessing before first chunk.** Pocket-tts (or our
   wrapper) may be running tokenization / phonemization / prosody planning
   over the *entire* input before emitting any audio.
2. **Sentence-batched generation.** If pocket-tts itself only streams
   *within* a sentence and emits sentences sequentially, the natural fix
   is parallel preprocessing of sentence N+1 while sentence N is being
   generated.
3. **Buffer / warmup cost.** First request after process start has a
   model-load tail. Confirm whether the 9 s case is first-call cold or
   persists on warm calls. If warm, structural; if only cold, expected.
4. **HTTP transport buffering.** Per ADR 0003 the speak transport is
   HTTP. Confirm we're using chunked transfer / streaming response, not
   buffering the whole audio body before the first byte hits the wire.

### Measurement methodology (pin this down before measuring anything)

Define once so this spike, the main-024 fix, and any future regression
share a reproducible protocol.

- **Time origin (T0):** the moment the HTTP `POST /speak` request line
  is sent. Capture HTTP-side timings via
  `curl -w "%{time_starttransfer},%{time_total}\n"`.
- **First-audio-at-speakers (T1):** wall-clock at first audible sample.
  Mark via a discrete cue — log line at the audio playback start
  callback, or a stopwatch around the audio device write. Document the
  chosen mechanism in the Outcome.
- **First-chunk latency = T1 − T0.** End-to-end, what the user hears.
- **Sample inputs** (canonical, repeatable, committed to repo under
  `examples/perf/`):
  - **Short:** "Hello, this is mockingbird." (5 words)
  - **Medium:** the same ~200-word paragraph used in main-018 verification
    — record verbatim in `examples/perf/medium-input.txt`.
  - **Long:** a ~1000-word paragraph — record verbatim in
    `examples/perf/long-input.txt`.
- **Cold vs warm:** "cold" = first call after a fresh sidecar boot;
  "warm" = at least one prior speak completed on the same sidecar
  process. Label every measurement.

### Diagnosis steps

1. Capture three timings using the methodology above:
   `cold-short`, `warm-short`, `warm-medium`. Repeat `warm-medium` 3× and
   report the median.
2. Run the same medium input directly through the **pocket-tts CLI**
   (no mockingbird involvement) and time the first chunk. If pocket-tts
   alone is ≤2 s, the entire ~7 s delta lives in our code.
3. With pocket-tts ruled in or out, walk the C# audio pipeline and the
   Python sidecar with logs / a profiler / strategic stopwatches to
   localise where the time goes. Hypotheses 1–4 are the suspect list;
   confirm or rule out each with evidence.
4. Write the **Outcome**: which hypothesis(es) won, file/line references,
   recommended fix shape (e.g. "audio buffer X is filled completely
   before first write — flush every N samples in `<file>:<line>`").

## Acceptance criteria

- [ ] Measurement methodology applied consistently — `curl -w` HTTP
  timings + first-audio mechanism documented and used for every
  measurement reported.
- [ ] Three end-to-end timings captured and recorded in Outcome:
  `cold-short`, `warm-short`, `warm-medium` (median of 3). Plus
  `warm-medium` against the pocket-tts CLI directly as the engine
  baseline.
- [ ] Each of the four hypotheses confirmed or ruled out, with
  evidence (log lines, code references, timing data).
- [ ] Outcome block written: diagnosis (which hypothesis won, where in
  code), recommended fix shape, expected scope of the fix.
- [ ] Sample inputs committed under `examples/perf/` so main-024 and
  future regression checks are reproducible.
- [ ] **main-024 acceptance criteria sharpened** based on diagnosis
  before this spike closes — at minimum, the file(s) main-024 will
  touch and the fix shape.

## Notes

- Reference: vision §What success looks like, ADR 0002 (Python sidecar),
  ADR 0003 (HTTP transport), main-018 Outcome.
- Pocket-tts upstream claims ~200 ms first-chunk; if our number is 9 s,
  the gap is almost certainly in *our* preprocessing/transport, not the
  model itself. The pocket-tts CLI baseline (step 2) is the
  load-bearing measurement here — it bisects "engine slow" vs "us slow".
- Out of scope: any code change to the speak path. If a fix is obvious
  during diagnosis (e.g. a single-line buffer flush), record it in the
  Outcome but do **not** apply it — main-024 lands the fix with proper
  before/after measurements.
- Does **not** block any other task — main-018 is closed; the page-set
  tasks are independent.
- The methodology is reusable: same measurement protocol applies to
  any future TTS perf concern.
