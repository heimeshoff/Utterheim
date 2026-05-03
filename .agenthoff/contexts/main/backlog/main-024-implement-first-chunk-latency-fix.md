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

main-023 diagnosed H4 (HTTP transport buffering) as the sole cause:
`_http.PostAsync(...)` at `src/Mockingbird/Services/Tts/PocketTtsEngine.cs:78`
uses the default `HttpCompletionOption.ResponseContentRead`, which
buffers the entire WAV before the awaited Task returns. Python's
`/tts` is already a proper `StreamingResponse` with chunked transfer
encoding — C# is throwing that streaming away.

### The fix

Replace line 78 with the streaming-completion form:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
using var response = await _http.SendAsync(
    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
```

Two lines instead of one (because `SendAsync` with options requires an
explicit `HttpRequestMessage`).

The existing 8 KB chunk-reader at `PocketTtsEngine.cs:86–102` does NOT
need to change — `ReadAsStreamAsync()` will now return a network-backed
stream that the loop already handles correctly. AudioPlayer also does
not need changes (it already plays chunks).

### Verify

Run the same `examples/perf/measure-latency.ps1` harness against the
same `examples/perf/*.txt` sample files that main-023 used as the
"before" baseline. main-023's Outcome table is your "before"; produce
an "after" table covering all four inputs.

Expected: first-audio time becomes input-length-independent, landing
in ~200–500 ms range. The `-Repeat 3` flag now becomes useful
(measurements should be tight; before was noisy because synthesis
time varied per content).

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

- **main-023 diagnosis complete (2026-05-04).** Single-file change at
  `PocketTtsEngine.cs:78`. ~10 minutes of work + measurement runs.
  Promotable now.
- Reference: main-023 (diagnosis), vision §What success looks like,
  ADR 0002 (Python sidecar), ADR 0003 (HTTP transport).
- Out of scope: full streaming-synthesis redesign, alternate engines,
  changes to streaming *correctness* (already working). This is a
  surgical "remove the ~7 s" change.
- Related: depending on root cause, may want to re-check ≤2 s for
  multi-paragraph Claude output (much longer than 1000 words). If the
  fix scales, no extra work; if it doesn't, follow-up.
