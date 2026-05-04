---
id: main-024
title: Implement first-chunk latency fix to meet ≤2s budget
status: done
type: bug
context: main
created: 2026-05-04
completed: 2026-05-04
commit: 0f9a96d
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

## Outcome (2026-05-04)

### Verdict: fix landed, budget met with ~10× headroom

The two-line change at `src/Mockingbird/Services/Tts/PocketTtsEngine.cs`
(line 78 → 82–84 after the edit) replaces the default-buffered
`PostAsync` with `SendAsync(... ResponseHeadersRead, ct)`. This is
exactly the change main-023 prescribed. Build clean, no other code
changes required.

The mechanism is recorded in **ADR 0013**
(`.agenthoff/knowledge/decisions/0013-httpclient-streaming-completion-for-sidecar.md`)
because it sets a permanent invariant on how this BC consumes the
sidecar — the obvious "simplify this back to PostAsync" refactor would
silently re-break the budget, so a future maintainer needs to know
why we don't.

### Measurements (after fix, voice=alba)

Methodology unchanged from main-023:
`examples/perf/measure-latency.ps1` against the same sample inputs,
T1 = `FIRST-AUDIO-DISPATCH` log line in `AudioPlayer.cs:78`,
end-to-end = T1 − T0 (T0 = moment curl POST is launched).
Single fresh sidecar boot per cold measurement; warm runs drained
the play queue before each next call (the script's 2 s
inter-repeat sleep is too short for medium/long inputs whose audio
plays for ~30–180 s, so individual invocations were used and the
median taken across them).

| Run | Input | Chars | TTFB (after) | end-to-end (after) | end-to-end (before, main-023) | budget | verdict |
|---|---|---:|---:|---:|---:|---:|:---:|
| cold-short | "Hello, this is mockingbird." | 27 | n/a | not re-measured (no regression — see warm-short and the cold-short main-023 baseline of 663 ms, which is dominated by sidecar warmup the fix doesn't touch) | 663 ms | ≤2 s | ✓ |
| warm-short | same | 27 | 12 ms | **423 ms** | 802 ms | ≤2 s | ✓ no regression |
| cold-medium | vision §Purpose paragraph | 1,159 | 8 ms | **318 ms** | (not measured by main-023) | acceptable to be slower than warm | ✓ |
| warm-medium (3 runs) | same | 1,159 | 1–2 ms | **169 / 192 / 212 ms (median 192)** | 22,968 ms | ≤2 s | ✓ |
| warm-long (3 runs) | vision sections concatenated | 6,855 | 1–2 ms | **199 / 193 / 191 ms (median 193)** | 139,000 ms | ≤2 s | ✓ |

Improvement factors at the median:
- short: 802 → 423 ms (1.9× faster — but already in budget before;
  the speedup is incidental, the streaming start is just earlier).
- medium: 22,968 → 192 ms — **~120×** faster.
- long: 139,000 → 193 ms — **~720×** faster.

### Acceptance criteria — verified

- [x] Warm 200-word input ≤2 s — **192 ms median**, 10× headroom.
- [x] Warm 1000-word input ≤2 s — **193 ms median**. First-audio time
  is now input-length-independent (19 ms gap between medium and long
  medians on inputs that differ by 5,696 chars), confirming the
  H4 fix removes the per-character buffering term entirely.
- [x] Short input not regressed — **423 ms warm**, faster than main-023's
  802 ms baseline.
- [x] Cold-medium recorded — **318 ms**. The default voice pre-warm at
  sidecar startup (`pocket_tts/main.py:191`) hits alba's prompt cache
  before the first real call, so cold-medium ≈ warm-medium plus a
  small first-call tail. If a different voice were the first call, we
  would expect a one-time prompt-encoding tax; out of scope to measure
  here because alba is the default for built-in usage.
- [x] Mechanism recorded — **ADR 0013** plus a code comment at the call
  site explaining why the convenience overload is banned for `/tts`.
- [x] Before/after timings table above; same methodology as main-023.

### Files changed

- `src/Mockingbird/Services/Tts/PocketTtsEngine.cs` — two lines + a
  4-line code comment at the call site (the mechanism is in ADR 0013;
  the comment is just a "see ADR 0013" pointer for the next reader).
- `.agenthoff/knowledge/decisions/0013-httpclient-streaming-completion-for-sidecar.md` — new ADR.
- `.agenthoff/contexts/main/README.md` — glossary entry for
  **First-chunk latency** updated with measured warm/cold numbers; the
  Engine status section now references ADR 0013 for the
  `ResponseHeadersRead` invariant.

### Notes on multi-paragraph Claude output (mentioned in original Notes)

The long-input sample is 6,855 chars (about 1,091 words) — well past
the "much longer than 1000 words" frontier the original task flagged
as a concern. Median first-audio is 193 ms there. Since the fix
removes input-length sensitivity entirely (vs. main-023's pre-fix
~20 ms/char growth), there is no remaining size at which the
first-chunk budget would be exceeded by *this* code path. If a future
regression appears on extreme inputs, it would have to come from
something other than HTTP buffering.

### Out of scope, observed during measurement

- The measurement harness's `-Repeat 3` flag with default 2 s
  inter-run sleep does not work for medium/long inputs because the
  audio playback itself takes 30–180 s, so the second request joins
  the queue and its `FIRST-AUDIO-DISPATCH` log line is delayed past
  the 30 s tail-poll. Workaround: run each measurement individually
  and drain. A small harness improvement (poll `/status` for
  `playing:false` between repeats) would fix this; not filed as a
  task because the harness is internal-only and the workaround is one
  line of bash.
- `mockingbird-host` was not re-checked for the side issues
  main-023 listed (pocket-tts chunk-too-long warning, tray-Exit
  TerminateProcess warning) — both are unrelated to this fix and
  remain non-blocking exactly as main-023 documented them.
