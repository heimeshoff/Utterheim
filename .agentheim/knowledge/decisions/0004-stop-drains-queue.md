---
id: 0004
title: Stop signal drains the queue by default
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-004, main-007, main-009]
related_research: []
---

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
- Vision: `.agentheim/vision.md` (open questions section)
- BC README: `.agentheim/contexts/main/README.md`
