---
id: main-024
title: Implement first-chunk latency fix to meet ≤2s budget
status: backlog
type: bug
context: main
created: 2026-05-04
completed:
commit:
depends_on: [main-023]
blocks: []
tags: [bug, performance, sidecar, streaming]
---

## Why

main-023 diagnoses where the ~7 s overhead lives in our speak pipeline.
This task implements the fix and verifies the latency target is met.

The vision target is ≤2 s first-chunk latency. main-018 verification
showed ~9 s on a warm 200-word input. Streaming *correctness* is
already satisfied (audio precedes synthesis end); only the latency
*budget* is missed.

## What

Apply the change main-023 recommends. Until that diagnosis lands, the
fix shape is unknown — likely candidates per main-023's hypotheses are:

- Chunk input by sentence/clause before handing to pocket-tts and
  stream chunk-by-chunk (Python sidecar change).
- Parallelise preprocessing of sentence N+1 while sentence N is being
  generated.
- Switch the HTTP response to chunked transfer / streaming if buffering
  is the culprit (C# pipeline change).
- A targeted buffer-flush change if the bottleneck is a single buffer
  in our path.

main-023's Outcome will pick the actual fix and sharpen the file/line
scope. **Do not start this task until that diagnosis exists.** When
main-023 closes, refine this task's `## What` to reflect the chosen
approach before promoting to `todo/`.

Use the measurement methodology and sample inputs that main-023
commits to `examples/perf/` for before/after numbers.

## Acceptance criteria

- [ ] First-chunk latency on a warm 200-word input (the
  `examples/perf/medium-input.txt` from main-023) is **≤2 s**
  end-to-end, measured per main-023's methodology.
- [ ] First-chunk latency on a warm 1000-word input
  (`examples/perf/long-input.txt`) is also ≤2 s — the latency must
  not scale with input length.
- [ ] A short input (≤20 words) remains ~1 s or better — no regression.
- [ ] Cold-call latency for the medium input is recorded in the
  Outcome (acceptable to be slower; the ≤2 s budget is for warm calls).
- [ ] Mechanism is recorded — either in this task's `## Outcome`
  block, or promoted to an ADR if main-023 flagged the change as
  architectural (e.g. transport-shape change).
- [ ] Before/after timings table in the Outcome, using the same
  methodology as main-023's diagnosis.

## Notes

- **Blocked on main-023.** Fix scope can't be sized until diagnosis
  lands. Acceptance criteria here are stable; the implementation
  approach is intentionally TBD.
- Reference: main-023 (diagnosis), vision §What success looks like,
  ADR 0002 (Python sidecar), ADR 0003 (HTTP transport).
- Out of scope: full streaming-synthesis redesign, alternate engines,
  changes to streaming *correctness* (already working). This is a
  surgical "remove the ~7 s" change.
- Related: depending on root cause, may want to re-check ≤2 s for
  multi-paragraph Claude output (much longer than 1000 words). If the
  fix scales, no extra work; if it doesn't, follow-up.
