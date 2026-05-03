---
id: main-022
title: Tray Exit leaves the python.exe sidecar alive as a zombie
status: backlog
type: bug
context: main
created: 2026-05-03
completed:
commit:
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
