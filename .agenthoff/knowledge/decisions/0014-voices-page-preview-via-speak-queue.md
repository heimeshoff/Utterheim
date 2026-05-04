---
id: 0014
title: Voices page preview routes through SpeakService.Enqueue (the single queue arbiter)
scope: bounded-context
context: main
status: proposed
date: 2026-05-04
supersedes: []
superseded_by: []
related_tasks: [main-014]
related_research: []
---

# ADR 0014: Voices page preview routes through SpeakService.Enqueue (the single queue arbiter)

## Context

main-014 (Voices page) ships a per-row Preview button that synthesises a
short canned phrase ("Hello, this is {voiceName}.") to let the user
audition a voice before assigning it to a Claude session. There are two
plausible code paths for that synthesis:

1. **Through the speak queue** — the page calls
   `SpeakService.Enqueue(canned, voiceId)`, identical to the Speak
   page's Play button (main-013, Q1) and to `POST /speak` from a Claude
   hook. The request goes onto the FIFO queue, the playback worker
   plays it, the existing stop hotkey halts it, the Speak-page status
   footer reflects it.

2. **Direct off-queue engine call** — the page calls
   `ITtsEngine.StreamAsync(...)` and pipes chunks to a side instance
   of `AudioPlayer` (or to the existing one out-of-band). This mirrors
   how `SpeakService.RenderToFileAsync` bypasses the queue to write a
   `.wav` file from the Save button.

The trade-off matters because mockingbird's queue invariant — "exactly
one playback worker decides what plays next" — is what makes the stop
semantics (ADR 0004) and the multi-Claude-session ordering story (ADR
0007) coherent. A second player would race the queue.

A concrete failure scenario: Claude session A POSTs a long read-aloud
to `/speak`. The user opens the Voices page mid-playback to audition
"marius". If Preview bypasses the queue, two voices play simultaneously
through the same output device. The user's stop hotkey would halt one
but not the other (or halt the wrong one, depending on which `cts` it
holds). The "single arbiter" property — the thing that lets the user
trust the stop gesture — is gone.

`RenderToFileAsync` is fine to bypass the queue precisely because it
*doesn't* play audio: it writes bytes to disk. There's no contention
for the output device, so there's no invariant to violate.

## Decision

Voices page Preview routes through **`SpeakService.Enqueue(cannedPhrase,
voiceId)`** — the same in-process seam main-013 established for the
Speak page's Play button and that `POST /speak` already uses.

Concretely:

```csharp
// In VoicesPageViewModel.PreviewAsync(VoiceRowViewModel row):
var phrase = $"Hello, this is {row.DisplayName}.";
_speakService.Enqueue(phrase, row.VoiceId);
```

No new code path, no new player, no new `cts`. The preview becomes a
regular speak request that happens to originate in the UI and carries a
canned text. The status footer (engine state + queue activity) already
reflects it via `SpeakService.StatusChanged`.

The canned phrase is hard-coded in the view-model for v1. If
internationalisation lands later, it moves to a resource string — out of
scope here.

## Consequences

### Positive

- **Single arbiter preserved.** The queue remains the only thing
  deciding what plays next. The stop hotkey, tray Stop, and `POST
  /stop` all halt the preview the same way they halt any other
  request. ADR 0004 stop semantics apply unchanged.
- **No double-player bug.** A Preview click while Claude is mid-utterance
  enqueues behind the active request (FIFO per ADR 0007). The user
  gets predictable ordering instead of overlapping voices.
- **Free status-footer integration.** The footer added in main-020
  already shows `synthesising → playing → idle` for queue activity.
  Preview light up the footer with no new wiring.
- **Free latency budget compliance.** First-chunk latency for the
  canned phrase is the same ~190 ms warm path the rest of the queue
  uses (per ADR 0013). No second tuning effort.
- **Reuses the validated path.** The `Enqueue → SpeakQueue →
  PocketTtsEngine.StreamAsync → AudioPlayer` pipeline is the most
  exercised code path in the app. Preview riding on it inherits all
  the regression tests and verification main-024 / main-018 produced.

### Negative

- **Preview cannot "barge" an active Claude utterance.** If Claude is
  reading a long passage and the user wants to audition a voice
  *immediately*, they must hit Stop first. This matches the FIFO
  decision in main-013 (Q1) and the vision's "decide after running
  sessions in anger" stance. If real usage shows the user wants
  preview to barge, that's a v1.5 lanes-on-the-queue feature — same
  decision point ADR 0007 already flagged.
- **Stop drains everything, including the preview.** If a preview is
  playing and the user hits double-tap LCtrl, the preview halts and
  any queued Claude requests behind it are discarded too (per ADR
  0004). Acceptable: the preview is a deliberate audition, the user's
  stop is a deliberate "be quiet."

### Trade-offs accepted

- **Audition latency = full queue latency.** A preview click while
  another request is in flight waits for that request to complete.
  For typical Claude utterances (~5–30 s) this is fine; the user
  preview-shopping through 10 voices would notice it. Mitigation:
  the user can hit Stop between preview clicks, or wait for v1.5
  lanes. Not worth a second player to optimise.

## Alternatives considered

- **Direct `ITtsEngine.StreamAsync` + side `AudioPlayer`** — rejected
  for the single-arbiter reasons above. The race against Claude
  utterances is not theoretical; it would happen the first time the
  user opens Voices while a hook is firing.
- **Direct `ITtsEngine.StreamAsync` + the existing `AudioPlayer`,
  guarded by a "is the queue idle?" check** — rejected: the check
  is racy (queue may dequeue between the check and the play call),
  and even when it passes, the result is "preview only works when
  nothing else is playing", which is *worse* UX than just enqueuing
  and waiting.
- **A separate "preview" priority lane on the queue** — rejected for
  v1. ADR 0007 deferred lanes to v1.5. Adding a lane just for
  preview prejudges the lanes design before we know what other lanes
  (notification cue vs read-aloud) actually want. Revisit if v1.5
  lanes land and preview wants to barge.
- **A `SpeakService.PreviewAsync(voiceId)` helper that wraps `Enqueue`
  with the canned phrase** — fine, but premature abstraction for v1.
  One call site, two lines of code. If a second preview surface
  appears (e.g., a Settings page voice tester), extract then.

## References

- ADR 0004: stop drains queue (single arbiter rationale).
- ADR 0007: speak queue as `Channel<T>` (FIFO, lanes deferred to
  v1.5).
- ADR 0013: streaming completion option (latency budget the preview
  inherits).
- main-013 doing/spec: `SpeakService.Enqueue` is the in-process seam.
- main-014: this task. Acceptance criteria pin the preview pathway to
  `SpeakService.Enqueue` so a future "simplify" refactor doesn't
  silently bypass the queue.
- BC README `## Engine status` and the styleguide `## Reusable
  component map` for the "Voices list with preview + delete
  affordances" component.
