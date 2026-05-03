---
id: main-019
title: Claude Code hook sample — make the speak endpoint actually used
status: todo
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-011, main-018]
blocks: []
tags: [integration, claude, docs]
---

## Why

The whole point of v1 (per the vision) is **Claude Code sessions calling
mockingbird's speak endpoint so the user gets audible cues per session**.
Without a documented, copy-pasteable hook recipe, the foundation is
unused. This task delivers the bridge from "the HTTP endpoint exists" to
"my Claude sessions actually talk to me".

Companion to main-011 — main-011 made the endpoint real, this makes
something call it.

## What

A small but complete integration kit, shipped in the mockingbird repo:

- **A `examples/claude-hooks/` directory** with at least:
  - A PowerShell script (or similar) that posts `{text, voice}` to
    `http://127.0.0.1:7223/speak`. Argument parsing for text, voice
    selection, optional silent-on-failure flag.
  - A README explaining how to wire it as a Claude Code Stop hook (so each
    session announces "task done" in its assigned voice) and as a
    Notification hook (for "input required" prompts).
  - Worked example: two parallel Claude sessions, each with a different
    voice, demonstrating the session-distinguishing-by-ear payoff.

- **Voice assignment guidance**: how does a session "know" its voice?
  Probably an env var the user sets per terminal (`MOCKINGBIRD_VOICE=marius`)
  that the hook script reads. Document the convention; don't try to bake
  it into the server.

- **A short troubleshooting section**: port conflicts, sidecar not running,
  Claude hook not firing.

## Acceptance criteria

- [ ] `examples/claude-hooks/` exists with at least one working sample
  script and a README.
- [ ] Following the README on a fresh Claude session produces audible
  "task done" messages in the assigned voice.
- [ ] Two parallel Claude sessions configured with different voices
  produce audibly different speech for the same hook event.
- [ ] Troubleshooting section covers the failure modes that surfaced
  during main-018 verification.

## Notes

- Reference: ADR 0003 (HTTP transport), Claude Code's hooks docs.
- Out of scope: any server-side support for "session identity" or routing.
  The voice routing happens entirely on the caller side via env-var
  convention. If we later want server-mediated routing, that's a separate
  decision task.
- Could ship before or after the page-set tasks — it's independent of the
  UI surface. Probably best after main-018 (so we know the engine works)
  and before main-013/14/15 (so the user gets the v1 payoff sooner).
