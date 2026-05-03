---
id: main-018
title: Clean-machine first-run verification of main-011
status: done
type: chore
context: main
created: 2026-05-01
completed: 2026-05-03
commit:
depends_on: [main-011, main-021, main-022]
blocks: [main-019]
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

- [x] All seven main-011 user-verifiable criteria checked off in this task's
  Outcome section, with evidence (log excerpts, timings, screenshots
  optional). 5 confirmed pass (1, 2, 5, 6 hard-tested; 3 streaming
  observed); 4 accepted-unverifiable (auto-restart races); 7, A, B
  assumed-pass pending future regression.
- [x] If any criterion fails, a follow-up bug task is filed in `backlog/`
  with reproduction steps. main-022 (zombie sidecar — fixed, commit
  `8a17ff9`) and main-023 (first-chunk latency — open in backlog).

## Notes

- Don't run on a VM unless audio output is actually plumbed — the `audible
  speech` criteria need a real speaker / headphone path.
- Probably worth doing this **before** committing to the page-set tasks
  (main-013 → main-017), since a sidecar failure here would invalidate the
  ground every page is built on.

## Outcome (verification paused — 2026-05-03)

First-run bootstrap **failed** during the user's first attempt at this
verification: the dialog reported `Python smoke test exited with code 1`
and refused to hand off to the tray. Reproduced on retry (eight failure
entries between 22:18:04 and 22:18:36 in
`%LOCALAPPDATA%\Mockingbird\logs\mockingbird-20260503.log`).

Root cause traced to two defects in `PythonRuntimeBootstrapper`:
- **Bug A** — step 3 (`InstallPocketTts`) is gated on the persisted
  `state.PocketTtsInstalled` flag only and does not verify
  `pocket_tts/__init__.py` on disk. When `bootstrap-state.json` outlives
  a wiped `runtime/` folder, step 3 silently skips and the smoke test
  runs against a runtime with no pocket-tts.
- **Bug B** — subprocess stderr is logged at `LogDebug`, so the actual
  Python traceback never reaches the log file. Failure surface was
  generic ("exited with code 1") with no diagnostic content.

Per this task's contract ("If anything fails, the right move is **not**
to fix it inside this task — file a bug task against main-011"), the
fix has been filed as **main-021** and added to this task's
`depends_on`. None of the seven user-verifiable acceptance criteria
above can be checked off until main-021 lands; verification is paused
until then.

Workaround for further manual probing in the meantime: delete
`bootstrap-state.json` alongside `runtime\` and `models\pocket-tts\`
before re-launching.

## Outcome (partial — 2026-05-03 23:30, after main-021 fix)

After main-021 landed (commit `75650d9`), verification re-attempted on
the same machine. Results so far:

| # | Criterion | Status | Notes |
|---|---|---|---|
| 1 | Bootstrap end-to-end → tray | ✅ pass | Python download + pocket-tts install completed cleanly; tray came up. |
| 2 | Warm `/speak` alba ≤2 s | ✅ pass | Short sentence audible in ~1 s after warmup. |
| 3 | Streaming on long input | ⚠️ pass-with-concern | 200-word paragraph: audio starts ~9 s in. Streaming *is* observable (audio precedes synthesis end), so this criterion passes. But ≤2 s vision target is missed badly. Filed as **main-023** (does not block this verification). |
| 4 | `/status` reflects degraded | ⚠️ unverifiable | Auto-restart respawns the sidecar before `/status` polling can observe `degraded`. Not filing — auto-restart resilience is the more important contract and it's working. |
| 5 | Sidecar logs flow through | ✅ pass | `[INF] sidecar INFO: …` lines present (Serilog source = "sidecar"; the `INFO:` prefix is from uvicorn's stdout, which is what we wanted to see captured). |
| 6 | Tray Exit kills `python.exe` | ✅ pass | After main-022 (commit `8a17ff9`, Win32 Job Object with KILL_ON_JOB_CLOSE), tray Exit reaps the sidecar process tree. User-confirmed 2026-05-03. |
| 7 | Half-finished bootstrap resumes | ✅ pass (assumed) | Not actively re-tested. `bootstrap-state.json` resume logic exists from main-011 and main-021 reconciles it against on-disk runtime; treating as working until a regression surfaces. User decision 2026-05-03. |
| A | main-021 spot-check: stale state file | ✅ pass (assumed) | Reconciliation logic landed in main-021 (`75650d9`); not re-exercised in this round. Treated as working until a regression surfaces. User decision 2026-05-03. |
| B | main-021 spot-check: stderr surfaces | ✅ pass (assumed) | LogInformation path for subprocess stderr landed in main-021 (`75650d9`); not re-exercised in this round. Treated as working until a regression surfaces. User decision 2026-05-03. |

Verification will resume once main-022 (zombie sidecar) is fixed —
criterion 6 is a hard fail. Criteria 7, A, and B can be done at the
same time. main-018 stays in `todo/` with `depends_on: [main-011,
main-021, main-022]`.
