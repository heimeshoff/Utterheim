---
id: main-023
title: First-chunk latency exceeds 2 s vision target on long input (~9 s observed for 200-word paragraph)
status: backlog
type: bug
context: main
created: 2026-05-03
completed:
commit:
depends_on: []
blocks: []
tags: [bug, performance, sidecar, streaming]
---

## Why

The vision sets a ≤2 s first-chunk-latency budget (vision §"What success
looks like" criterion 1, plus the seed glossary entry for
**First-chunk latency**: "Target ≤2 s; pocket-tts claims ~200 ms").

During main-018 first-run verification:
- Short sentence ("Hello, this is mockingbird.") → ~1 s to audible. Within budget.
- ~200-word paragraph → **~9 s to audible**. ~4.5× over budget.

Streaming itself is working — playback starts before synthesis completes,
satisfying main-018's *correctness* criterion 3. But the vision-level
*latency* target is missed badly enough that the read-aloud experience
will feel sluggish in the v1 scenario it's designed for.

## What

Investigate why first-chunk latency scales with input length and bring
it under 2 s for typical Claude-Code-output sized inputs (a few hundred
words).

Hypotheses to probe in roughly this order:

1. **Whole-text preprocessing before first chunk.** Pocket-tts (or our
   wrapper) may be running tokenization / phonemization / prosody planning
   over the *entire* input before emitting any audio. If so, audio output
   shouldn't have to wait for that — chunk the input first (sentence- or
   clause-level) and stream chunk-by-chunk.

2. **Sentence-batched generation.** If pocket-tts itself only streams
   *within* a sentence but produces sentences sequentially, the natural
   fix is to start producing audio for sentence 1 while preprocessing
   sentence 2 in parallel.

3. **Buffer / warmup cost.** The first request after process start has a
   model-load tail; this is expected. Confirm whether the 9 s case is
   first-call cold or whether it persists on warm calls. If warm, it's
   structural, not warmup.

4. **HTTP transport buffering.** ADR 0003 chose HTTP for the speak
   transport; confirm we're using chunked transfer / streaming response,
   not buffering the whole audio body before the first byte hits the
   wire.

The work likely lives partly in the C# audio pipeline (how we read the
streaming response and feed the playback device) and partly in the
Python sidecar (how it segments input before handing to pocket-tts).

## Acceptance criteria

- [ ] First-chunk latency on a warm 200-word input is **≤2 s** end-to-end
  (POST → first audio sample at speakers).
- [ ] First-chunk latency on a warm 1000-word input is also ≤2 s
  (the latency must not scale with input length).
- [ ] A short input (≤20 words) remains ~1 s or better — no regression.
- [ ] Cold-call latency is documented (acceptable to be slower; the
  budget is for warm calls).
- [ ] Mechanism is recorded in an ADR or a `## Outcome` block —
  whatever change unblocked the latency, future changes should know.

## Notes

- Reference: vision §What success looks like, ADR 0002 (Python sidecar),
  ADR 0003 (HTTP transport), main-018 Outcome.
- Pocket-tts upstream claims ~200 ms first-chunk; if our number is 9 s,
  the gap is almost certainly in *our* preprocessing/transport, not the
  model itself. Worth a quick check against the pocket-tts CLI directly
  to confirm baseline.
- Out of scope: full streaming-synthesis redesign, alternate engines.
  This is a "find what's adding 7 s and remove it" task.
- Does **not** block main-018 — main-018 criterion 3 is satisfied by
  observed streaming. This is a vision-level perf concern that can land
  any time before v1 ships.
- Related: depending on root cause, may want to re-check the ≤2 s target
  for read-aloud of multi-paragraph Claude output (much longer than 200
  words). If the fix scales, no extra work; if it doesn't, follow-up.
