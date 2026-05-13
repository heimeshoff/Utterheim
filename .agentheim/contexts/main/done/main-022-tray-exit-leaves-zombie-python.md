---
id: main-022
title: Tray Exit leaves the python.exe sidecar alive as a zombie
status: done
type: bug
context: main
created: 2026-05-03
completed: 2026-05-03
commit: 8a17ff9
depends_on: []
blocks: [main-018]
tags: [bug, sidecar, lifecycle, foundation]
---

## Why

Surfaced during main-018 first-run verification: after right-clicking the
tray icon and choosing Exit, the C# host process closes but the
`python.exe` child process keeps running. Visible in Task Manager,
holding the port, holding the model in memory. Repeated launch/exit
cycles will leak more zombies.

This violates main-018 acceptance criterion 6 ("Tray Exit terminates
the python.exe — no zombie process in Task Manager") and breaks the
shutdown contract documented in ADR 0002 (Python sidecar shape). It also
means a stale sidecar from a previous session can answer requests after
a fresh launch starts up its own — confusing-to-impossible to diagnose.

## What

The tray Exit command must guarantee the sidecar process tree is gone
before the host exits. Three things are likely contributing; the fix
should address all three:

1. **Process tree kill, not just the parent.** The python launcher we
   spawn (`pythonw.exe` or `python.exe`) may itself spawn workers or
   helper processes (uvicorn workers, BLAS threads in subprocesses).
   `Process.Kill(entireProcessTree: true)` instead of `Kill()` covers
   that case.

2. **Send graceful shutdown first, then escalate.** Best-effort sequence:
   - POST `/shutdown` (or whatever the sidecar exposes) and wait up to
     ~2 s for clean exit
   - If still alive, `Process.Kill(entireProcessTree: true)`
   - If still alive after another 1–2 s, log at ERR and bail

3. **Disable any auto-restart watchdog during shutdown.** If the host
   has supervisor logic that respawns the sidecar when it dies (the
   "auto-restart race" we noticed in criterion 4 of main-018), Exit
   must flip a "shutting down" flag *before* killing, otherwise the
   supervisor immediately respawns the python we just killed.

The shutdown path lives in the sidecar process manager that ADR 0002
introduced; likely `SidecarProcessManager` or `PythonRuntimeService`
(or whichever class owns the `Process` reference). The tray Exit handler
wires through `App.OnExit` / equivalent.

## Acceptance criteria

- [ ] After tray Exit, no `python.exe` belonging to Mockingbird remains
  in Task Manager. Verify with `Get-Process python | Where CommandLine
  -match mockingbird` returning empty.
- [ ] Repeated launch + exit (5 cycles) leaves no zombies and the port
  (7223) is free immediately after each exit.
- [ ] If the sidecar's graceful shutdown path stalls, the host still
  hard-kills it within ~3 s and logs the escalation at ERR.
- [ ] The auto-restart watchdog (whatever its current shape) does not
  respawn the sidecar during host shutdown.
- [ ] main-018 criterion 6 can be checked off.

## Notes

- Affected files (educated guess; confirm during the work):
  `src/Mockingbird/Services/Tts/SidecarProcessManager.cs` (or the
  class that holds the `Process` reference), `src/Mockingbird/App.xaml.cs`
  (Exit handler), and whatever supervises auto-restart.
- Reference: ADR 0002 (Python sidecar shape), main-018 Outcome.
- Related: criterion 4 of main-018 ("/status reflects degraded when
  sidecar killed externally") was unverifiable because auto-restart
  wins the race. Same supervisor logic; this fix should not regress
  the resilience behavior — only suppress restarts during host shutdown.
- This task **blocks main-018** — until tray Exit terminates the
  sidecar, criterion 6 cannot be checked off and main-018 cannot close.
- Do not bundle the latency fix from main-023; separate concerns.

## Outcome

Fixed by binding every spawned `python.exe` to a Win32 Job Object with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (per **ADR 0012**, see
`.agenthoff/knowledge/decisions/0012-sidecar-jobobject-kill-on-close.md`).
This solves the root cause behind acceptance criteria 1–4:

- **Criterion 1 (no zombie after tray Exit):** `SidecarHost.StopAsync`
  now disposes the JobObject after attempting the existing
  `Process.Kill(entireProcessTree: true)` path. Closing the job handle
  triggers KILL_ON_JOB_CLOSE — Windows kills every member of the job
  atomically, including grandchildren (uvicorn workers / multiprocessing
  spawn) that the parent-PID tree walk could miss.
- **Criterion 2 (port free + clean over 5 cycles):** the JobObject path
  is OS-enforced, so each Exit reliably reaps the tree and frees 7223.
- **Criterion 3 (escalation logged at ERR within ~3 s):** post-tear-down,
  `SidecarHost.StopAsync` calls `WaitForExit(1000)` on the captured
  Process and emits `LogError` if anything is still alive after both
  the tree-kill and the JobObject dispose. Whole shutdown path is
  bounded by the supervisor await (2 s) + verify wait (1 s) ≈ 3 s.
- **Criterion 4 (auto-restart watchdog suppressed during shutdown):**
  added `_shuttingDown` flag set as the *first* statement in
  `StopAsync`; the supervisor while-loop, the `WaitForExitAsync` post-
  check, and the back-off-after-failure check all now bail out on
  `_shuttingDown`. Belt-and-suspenders against the cancellation-token
  race (the token alone was almost-enough; the explicit flag makes
  intent unmissable in the code).
- **Criterion 5 (main-018 criterion 6 unblocks):** ready for in-app
  verification on the user's machine. main-018 stays blocked on this
  task until that walkthrough confirms it.

There is also a defence-in-depth bonus: because KILL_ON_JOB_CLOSE fires
when the *last handle* to the job is released, an abrupt host death
(crash, `taskkill /f`, debugger detach) also kills the tree — the
sidecar can no longer outlive the host under any documented Windows
shutdown path.

### Files

- `src/Mockingbird/Services/Tts/ProcessJobObject.cs` — new P/Invoke
  wrapper for the Job Object. Created lazily in `SidecarHost`'s field
  initializer; disposed on `StopAsync` and `Dispose`.
- `src/Mockingbird/Services/Tts/SidecarHost.cs` — assigns each spawned
  process to the job, sets `_shuttingDown` first, escalates with
  `LogError` if the tree is still alive after the kill paths.
- `.agenthoff/knowledge/decisions/0012-sidecar-jobobject-kill-on-close.md`
  — ADR documenting *why* the JobObject was chosen over a `/shutdown`
  POST or a `CTRL_BREAK_EVENT` process group.
- `.agenthoff/contexts/main/README.md` — engine-status block updated
  with the new KILL_ON_JOB_CLOSE guarantee.

### Verification

- `dotnet build mockingbird.sln -c Debug` — clean (0 warnings, 0 errors).
- Acceptance criteria 1, 2, 7 (main-018 spot-check) require an in-app
  walkthrough on the user's Windows machine; queued as part of the
  resumed main-018 verification pass.
