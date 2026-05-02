---
id: main-018
title: Clean-machine first-run verification of main-011
status: backlog
type: chore
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-011]
blocks: []
tags: [verification, foundation]
---

## Why

main-011 (real pocket-tts engine bootstrap) was marked done with build-clean
but the user-verifiable acceptance criteria are explicitly **pending first
run on a clean machine** (per the `## Outcome` block in
`done/main-011-pocket-tts-real-bootstrap.md`). Until that run happens,
"the engine works" is a code-review claim, not a tested fact. This task
closes that loop.

## What

Run mockingbird on a machine with no prior `%LOCALAPPDATA%\Mockingbird\runtime\`
or `models\pocket-tts\` directories — either by deleting those folders on the
dev machine or by running on a fresh VM / second machine — and walk through
every pending criterion from main-011:

- Bootstrap dialog runs end-to-end, downloads Python 3.12.7 embeddable +
  `pocket-tts>=2.0,<3` (~700 MB), persists progress, hands off to the tray.
- `curl -X POST http://127.0.0.1:7223/speak -d '{"text":"Hello, this is
  mockingbird.","voice":"alba"}'` produces audible alba within ~2 s of the
  first warm call.
- Long text (~200 words) starts audible playback before synthesis completes
  (streaming observable).
- `GET /status` reports `sidecar.state = "running"` and `sidecar.healthy = true`
  once warm; reflects degraded state if the sidecar is killed externally.
- Sidecar stdout/stderr appear in `mockingbird-YYYYMMDD.log` under the
  `sidecar` source.
- Tray "Exit" terminates the python.exe — no zombie process in Task Manager.
- Half-finished bootstrap (kill mid-download) resumes correctly on next
  launch via `bootstrap-state.json`.

Capture the results in this task file under `## Outcome`, with timings,
actual download size, and any unexpected behaviour. If anything fails,
the right move is **not** to fix it inside this task — file a bug task
against main-011 and reopen the engine work properly.

## Acceptance criteria

- [ ] All seven main-011 user-verifiable criteria checked off in this task's
  Outcome section, with evidence (log excerpts, timings, screenshots
  optional).
- [ ] If any criterion fails, a follow-up bug task is filed in `backlog/`
  with reproduction steps.

## Notes

- Don't run on a VM unless audio output is actually plumbed — the `audible
  speech` criteria need a real speaker / headphone path.
- Probably worth doing this **before** committing to the page-set tasks
  (main-013 → main-017), since a sidecar failure here would invalidate the
  ground every page is built on.
