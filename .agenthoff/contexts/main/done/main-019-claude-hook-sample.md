---
id: main-019
title: Claude Code hook sample — make the speak endpoint actually used
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-03
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

- [x] `examples/claude-hooks/` exists with at least one working sample
  script and a README.
- [~] Following the README on a fresh Claude session produces audible
  "task done" messages in the assigned voice. *Audible verification is
  the user's job at hand-off — see Outcome.*
- [~] Two parallel Claude sessions configured with different voices
  produce audibly different speech for the same hook event. *Same — the
  worked-example recipe is in the README; the user runs it.*
- [x] Troubleshooting section covers the failure modes that surfaced
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

## Outcome

Delivered the integration kit under `examples/claude-hooks/`:

- `examples/claude-hooks/mockingbird-hook.ps1` — PowerShell shim that
  POSTs `{text, voice}` to the speak endpoint. Resolves voice in this
  order: `-Voice` parameter → `$env:MOCKINGBIRD_VOICE` → `alba`.
  Resolves endpoint in this order: `-Endpoint` parameter →
  `$env:MOCKINGBIRD_ENDPOINT` → `http://127.0.0.1:7223` (ADR 0003
  default). `-Silent` switch swallows all failures and exits 0, so a
  missing sidecar never blocks Claude Code's own flow. Distinct exit
  codes (0 / 2 / 3) for success / HTTP error / unreachable when not
  silent. Saved as UTF-8 with BOM so Windows PowerShell 5.1 parses the
  em-dashes correctly. Syntax-validated and smoke-tested locally
  (silent path → exit 0; loud path → reported `cannot reach …` and
  exited non-zero, as designed).
- `examples/claude-hooks/README.md` — wiring recipe for Claude Code's
  `Stop` (task done) and `Notification` (input required) hooks; the
  per-terminal `MOCKINGBIRD_VOICE` env-var convention; the worked
  two-parallel-sessions example; and a troubleshooting section that
  covers the failure modes surfaced by main-018 verification — sidecar
  not running, `Engine: failed` after watchdog gives up, the
  ~9 s cold first-chunk latency tracked as main-023, the
  watchdog-vs-`/status`-polling race, port-7223 collision, env var set
  after `claude` already launched. The hook-config JSON is shown as a
  representative shape with an explicit "verify against your Claude
  Code version" caveat — not fabricated as canonical, since the schema
  has shifted between Claude Code versions and was not safely
  determinable from this sandbox.

Also updated `.agenthoff/contexts/main/README.md` with a new "Claude Code
integration kit" section so future sessions discover the bridge from
domain memory.

**Audible verification is the user's job at hand-off.** Acceptance
criteria 2 and 3 require a fresh Claude session and real speakers; this
task ships the recipe correct enough that following it produces the
intended result. If the recipe doesn't work on the user's Claude Code
version, the troubleshooting section and the explicit version-caveat in
the hook-wiring section point the way.

No ADRs written: every decision (transport, port, wire format,
session-identity-is-caller-side) was already frozen by ADR 0003 and the
BC README. No new backlog items: nothing surfaced during the work that
isn't already tracked (main-023 covers the cold-start latency the
troubleshooting section warns about).

Key files:

- `c:\src\heimeshoff\containers\mockingbird\examples\claude-hooks\mockingbird-hook.ps1`
- `c:\src\heimeshoff\containers\mockingbird\examples\claude-hooks\README.md`
- `c:\src\heimeshoff\containers\mockingbird\.agenthoff\contexts\main\README.md`
  (added "Claude Code integration kit" section)
