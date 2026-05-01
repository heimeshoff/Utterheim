---
id: main-007
title: Speak queue lives in the C# host as a Channel<T>
status: done
type: decision
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: [main-002, main-004]
blocks: [main-009]
tags: [foundation, concurrency, queue]
---

## Why

The vision mandates FIFO queueing of speak requests across parallel Claude sessions. The queue's home (C# host vs Python sidecar) determines where stop-cancellation and engine-routing live. Wrong placement would force one side to know about the other's invariants.

## What

Place the queue in the C# host as `System.Threading.Channels.Channel<SpeakRequest>` with `SingleReader = true`, `SingleWriter = false`, `Unbounded` for v1. Each `SpeakRequest` carries `{requestId, text, voiceId, enqueuedAt, cts}`. The Python sidecar stays stateless w.r.t. ordering — one synthesise call → one audio stream.

## Acceptance criteria

- [ ] ADR 0007 committed at `.agenthoff/knowledge/decisions/0007-queue-channel-in-host.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries user amendments).
- [ ] No code yet — implementation lands in main-009.

## Notes

Why the queue must live on the C# side:

- HTTP endpoint receives `POST /speak`, must immediately return queue position. Sidecar is a per-utterance worker, not an ordering authority.
- Stop signal is a Win32 hotkey on the C# side; needs to cancel the in-flight sidecar call AND drain the queue atomically.
- Multi-engine future-proofing: the queue dispatches to whichever engine the requested voice is bound to; only C# knows the engine map.

Open follow-ups (deferred to v1.5):
- Priority lanes (notification cue lane vs read-aloud lane). Add a second `Channel<T>` and arbitration when usage shows it's needed.
- Backpressure cap (e.g., 100 entries, 503 response on overflow). Only if a buggy/spammy hook produces flood scenarios.

Full ADR draft (drop into `0007-queue-channel-in-host.md`):

```markdown
# ADR 0007: Speak queue lives in the C# host as a Channel<T>

## Context
The vision mandates FIFO queueing of speak requests across parallel Claude sessions. Architecture options for queue placement: (a) C# in-process queue, (b) inside the pocket-tts Python sidecar. The stop-signal hotkey, the engine-routing logic (future multi-engine), and the HTTP endpoint all live on the C# side; the sidecar is a per-utterance worker.

## Decision
The speak queue is a `System.Threading.Channels.Channel<SpeakRequest>` owned by the C# host:
- `SingleReader = true` (one playback worker dequeues), `SingleWriter = false` (multiple HTTP request threads enqueue), `Unbounded` for v1.
- Each `SpeakRequest` carries `{requestId, text, voiceId, enqueuedAt, cts}`.
- The playback worker is a long-running `Task` that loops: `await _channel.Reader.ReadAsync(stopAllCts.Token)` → resolve voice → call sidecar → stream audio to NAudio output → mark complete.
- Stop-current: cancel `request.cts`. Stop-and-drain (per ADR 0004): cancel `request.cts` AND drain `_channel.Reader.ReadAllAsync()` until empty.
- The Python sidecar is stateless w.r.t. queue ordering — it answers one synthesise call at a time. If we ever add a second engine, the C# dispatcher picks the right sidecar / in-process worker per voice.

## Consequences
### Positive
- The full ordering and cancellation story is in one process, in one language.
- `Channel<T>` is the modern, async-friendly primitive — clean code, no locks.
- Stop semantics map cleanly to CancellationToken plumbing.
- Ready for multi-engine routing without restructuring the queue.

### Negative
- The sidecar has no awareness of the queue; if the sidecar dies mid-utterance, the C# side observes a faulted Task and decides whether to retry or skip.

### Neutral
- Backpressure for spam scenarios is deferred (vision is single-user); easy to add via bounded channel later.

## Alternatives considered
- **Queue inside the Python sidecar** — rejected: the sidecar would need to know about the stop hotkey, the multi-engine future, and the HTTP front door. Sidecar should stay narrow.
- **`BlockingCollection<T>`** — rejected: blocking semantics are awkward in an async-first WPF app. `Channel<T>` is the modern replacement.
- **Multiple lanes (notification cue lane vs read-aloud lane)** — vision marks this as a "decide after running sessions in anger." Defer to v1.5; the `Channel<T>` design doesn't preclude adding lanes (one channel per priority).

## References
- Vision: `.agenthoff/vision.md`
- Stop semantics: ADR 0004
- Sidecar shape: ADR 0002
```

## Outcome

ADR 0007 written at `.agenthoff/knowledge/decisions/0007-queue-channel-in-host.md` per the draft above. Queue placement decision is now locked: C# host owns the FIFO `Channel<SpeakRequest>`, sidecar stays stateless. Implementation deferred to main-009 (which is unblocked by this decision).

Key file:
- `.agenthoff/knowledge/decisions/0007-queue-channel-in-host.md`
