---
id: 0012
title: Bind the python sidecar to a Win32 Job Object with KILL_ON_JOB_CLOSE
scope: main
status: accepted
date: 2026-05-03
supersedes: []
superseded_by: []
related_tasks: [main-022, main-018]
related_research: []
---

# ADR 0012: Bind the python sidecar to a Win32 Job Object with KILL_ON_JOB_CLOSE

## Context

ADR 0002 made the python pocket-tts engine a sidecar process the C# host
supervises, with "graceful shutdown on tray exit" as part of the contract.
main-018 first-run verification (criterion 6) caught that this contract
was broken in practice: tray Exit closed the host but left `python.exe`
running as a zombie, holding port 7223 and ~600 MB of model memory.

The original implementation relied on
`Process.Kill(entireProcessTree: true)` and a tree-walking parent-PID
sweep. That approach has two well-known races on Windows:

1. **Grandchild re-parenting.** Pocket-tts's launcher may spawn uvicorn
   workers / multiprocessing helpers. If a grandchild's parent dies first
   it gets re-parented to System and the tree walk loses it.
2. **Abrupt host death.** If the host crashes or is killed (`taskkill /f`,
   debugger detach), no .NET shutdown path runs at all and every spawned
   python keeps running indefinitely.

Both leak processes that hold the loopback port — confusing-to-impossible
to diagnose because a stale sidecar from yesterday answers requests that
should have hit today's freshly-spawned one.

## Decision

Create a single Win32 Job Object owned by `SidecarHost` and assign every
spawned python process to it before the python has a chance to spawn
children. Configure the job with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.

This gives us atomic, OS-enforced cleanup along three paths:

- **Normal shutdown (tray Exit):** `SidecarHost.StopAsync` disposes the
  job after the supervisor unwinds; Windows kills every member of the
  job in one call. Tree-walk `Process.Kill` is kept as the first attempt
  so logs stay informative, but the job dispose is the load-bearing step.
- **Host crash / kill:** when the host process dies the OS releases its
  handle to the job. Once the last open handle to a job closes,
  `KILL_ON_JOB_CLOSE` fires and Windows kills every still-running
  member. Zero zombies even if the host's shutdown path never ran.
- **Belt-and-suspenders auto-restart suppression:** a `_shuttingDown`
  flag is set as the *first* thing in `StopAsync`, so the supervisor
  loop refuses to respawn even if it observes the python exit before
  noticing the cancellation token. (The token alone is enough in
  principle, but the explicit flag closes the race window where the
  supervisor races past `ct.IsCancellationRequested` and into the
  restart branch.)

A 1 s post-verification waits on the captured Process handle after both
kill paths have fired and logs at `LogError` if anything is still alive
— giving operators a clear signal in
`%LOCALAPPDATA%\Mockingbird\logs\` if the contract ever degrades again
(per main-022 acceptance criterion 3).

The job is created in the constructor (lazy via field initializer) and
disposed in `Dispose()` so even direct DI-container teardown without a
prior `StopAsync` releases the OS handle and triggers cleanup.

## Consequences

### Positive

- `python.exe` cannot survive the host process under any documented
  Windows shutdown path (graceful, abrupt, or debugger-detach).
- Grandchildren (uvicorn workers, BLAS subprocess pools, multiprocessing
  spawn) are caught automatically — no need to enumerate them.
- The fix is local to `Services/Tts/SidecarHost.cs` and a small
  P/Invoke helper; no changes to engine, transport, or UI surfaces.

### Negative

- ~150 lines of P/Invoke (`ProcessJobObject.cs`) we now own. The
  surface is small and stable across Windows versions; risk is low.
- If a child process voluntarily calls `CreateJobObject` itself and is
  not nested-job-aware, `AssignProcessToJobObject` would historically
  fail with `ERROR_ACCESS_DENIED`. Windows 8+ supports nested jobs out
  of the box; we target Windows 10/11 (`net9.0-windows`, x64), so this
  is moot. The code logs at `LogError` and continues if assignment ever
  does fail, so the host degrades gracefully (back to the
  `Process.Kill(tree)` path) rather than refusing to start.

### Neutral

- The previous tree-walk kill path is preserved as the first attempt;
  removing it would also work but keeping it gives clearer log lines
  when only the parent needs to die (the common case).

## Alternatives considered

- **POST `/shutdown` to the sidecar first.** The pocket-tts FastAPI
  surface does not expose a shutdown endpoint, and uvicorn's signal
  handling on Windows is unreliable for non-console children. A
  best-effort graceful POST would only have served as a "log nicer
  banner" feature; not worth the round-trip cost on every Exit.
- **Send `CTRL_BREAK_EVENT` to a process group.** Requires spawning
  with `CREATE_NEW_PROCESS_GROUP` and a console — incompatible with
  `CreateNoWindow = true`. Also doesn't solve the abrupt-host-death
  case.
- **Polling Task Manager / WMI to clean orphans on next launch.**
  Fragile; assumes mockingbird relaunches; doesn't free the port
  immediately after Exit.

## References

- main-022: Tray Exit leaves the python.exe sidecar alive as a zombie
- main-018 outcome 2026-05-03: criterion 6 fail report
- ADR 0002: Run pocket-tts as a managed Python sidecar over loopback HTTP
- Microsoft docs: [JobObject API](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information)
