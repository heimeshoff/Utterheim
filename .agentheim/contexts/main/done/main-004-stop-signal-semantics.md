---
id: main-004
title: Stop signal drains the queue by default (configurable)
status: done
type: decision
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: []
blocks: [main-007, main-009]
tags: [foundation, ux, hotkey]
---

## Why

The vision flags stop-signal semantics as an explicit open question. Resolving it is necessary for a coherent walking skeleton: the queue mechanism (main-007) and the HTTP `/stop` endpoint (main-003) both need to know what "stop" means.

## What

Default behaviour: **stop current utterance AND drain the speak queue**. Expose a settings toggle so the user can switch to "stop current only" after experience. Implement via a single cancellation token at the queue level.

## Acceptance criteria

- [ ] ADR 0004 committed at `.agenthoff/knowledge/decisions/0004-stop-drains-queue.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries user amendments).
- [ ] No code yet — implementation lands in main-009.

## Notes

User mental model when hitting double-tap LCtrl: "I want to respond" or "be quiet, I need to think." Both align with "silence the room, not just this paragraph." Dramaturgically, drain matches intent.

Counter-case: skip-this-paragraph during long read-aloud. Mitigation deferred to v1.5 — a "skip" hotkey (single-tap or different combo). Don't add until the user asks.

Full ADR draft (drop into `0004-stop-drains-queue.md`):

```markdown
# ADR 0004: Stop signal drains the queue by default

## Context
Vision spec: double-tap LCtrl is the stop-signal. The semantics are explicitly flagged as an open question — does it (a) only stop current, (b) stop current and drain the queue, or (c) configurable? The user's mental model when hitting the hotkey is "I want to respond" or "be quiet, I need to think" — both align with "silence the room, not just this paragraph."

## Decision
v1 default: **stop current utterance AND drain the speak queue** (option b). The setting is exposed in the tray UI so the user can switch to "stop current only" if they prefer the alternative model after a few weeks of use (option c).

Implementation: on stop signal, the synthesis engine cancels in-flight generation, the audio output device flushes its buffer, and the queue's pending requests are discarded. A toast / tray badge briefly indicates "queue cleared (N items)".

## Consequences
### Positive
- Matches the dominant user intent ("give me silence").
- Predictable: the same gesture always ends in silence.
- Simple implementation: one cancellation token at the queue level.

### Negative
- Users who want skip-to-next-cue need a second hotkey (deferred to v1.5).
- Drain is destructive and not undoable — a queued long read-aloud is gone.

### Neutral
- The toast / badge "queue cleared (N items)" is enough feedback for now.

## Alternatives considered
- **Stop current only** — kept as a configurable opt-in. May become the default if real-world usage shows drain is too aggressive.
- **Configurable from day one with no default** — rejected; chose a clear default to avoid analysis paralysis at v1.

## References
- Vision: `.agenthoff/vision.md` (open questions section)
```

## Outcome

Decision recorded as ADR 0004 (`scope: global`, `status: accepted`). Stop semantics for v1: cancel in-flight synthesis, flush audio buffer, and discard pending queue items; setting toggle exposed in tray UI to allow opting into "stop current only". Implementation deferred to main-009; queue mechanism (main-007) and HTTP `/stop` endpoint (main-003) can now build against this decision.

Key files:
- `.agenthoff/knowledge/decisions/0004-stop-drains-queue.md`
