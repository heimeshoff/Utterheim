---
id: 0007
title: Speak queue lives in the C# host as a Channel<T>
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-007, main-009]
related_research: []
---

# ADR 0007: Speak queue lives in the C# host as a Channel<T>

## Context
The vision mandates FIFO queueing of speak requests across parallel Claude sessions. Architecture options for queue placement: (a) C# in-process queue, (b) inside the pocket-tts Python sidecar. The stop-signal hotkey, the engine-routing logic (future multi-engine), and the HTTP endpoint all live on the C# side; the sidecar is a per-utterance worker.

## Decision
The speak queue is a `System.Threading.Channels.Channel<SpeakRequest>` owned by the C# host:
- `SingleReader = true` (one playback worker dequeues), `SingleWriter = false` (multiple HTTP request threads enqueue), `Unbounded` for v1.
- Each `SpeakRequest` carries `{requestId, text, voiceId, enqueuedAt, cts}`.
- The playback worker is a long-running `Task` that loops: `await _channel.Reader.ReadAsync(stopAllCts.Token)` -> resolve voice -> call sidecar -> stream audio to NAudio output -> mark complete.
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
- Vision: `.agentheim/vision.md`
- Stop semantics: ADR 0004
- Sidecar shape: ADR 0002
