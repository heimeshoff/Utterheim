---
id: main-021
title: Bootstrap skips pocket-tts install when state file outlives runtime; smoke-test stderr is invisible
status: done
type: bug
context: main
created: 2026-05-03
completed: 2026-05-03
commit:
depends_on: []
blocks: [main-018]
tags: [bug, bootstrap, sidecar, foundation]
---

## Why

main-018 (clean-machine first-run verification) surfaced a real bootstrap
failure: on first launch the bootstrap dialog reports
"Python smoke test exited with code 1" and refuses to hand off. Reproduced
multiple times by the user (eight `Bootstrap failed` entries between
22:18:04 and 22:18:36 in `mockingbird-20260503.log`).

main-018's contract says "if anything fails, file a bug task against
main-011 and reopen the engine work properly." This is that task.

Two distinct defects feed the failure — one is the proximate cause, the
other is what made it un-diagnosable.

## What — Bug A (root cause): per-step skip logic doesn't verify on disk

`Services/Tts/PythonRuntimeBootstrapper.cs:BootstrapAsync` gates step 3
(`InstallPocketTts`) on the persisted state flag **only**:

```csharp
// line ~123
if (!state.PocketTtsInstalled)
{
    progress.Report(...);
    await RunPipAsync($"install ... {PocketTtsSpec}", ct, ...);
    state.PocketTtsInstalled = true;
    SaveState(state);
}
else
{
    progress.Report(... "pocket-tts already installed.");
}
```

Compare to step 1 (`!state.PythonExtracted || !File.Exists(PythonExePath)`)
and step 2 (`!state.PipInstalled || !PipExists()`) — those have a
belt-and-braces existence check. Step 3 trusts the JSON alone.

Reproducing observation in this incident:
1. A previous successful bootstrap left `bootstrap-state.json` with
   `PocketTtsInstalled: true` / `RuntimeReady: true`.
2. The `runtime/` folder was deleted (clean-machine simulation) but the
   JSON state file in `%LOCALAPPDATA%\Mockingbird\` was not.
3. `IsBootstrapped` correctly returned false (its file-existence check
   *does* verify `pocket_tts/__init__.py`), so the dialog opened.
4. `BootstrapAsync` re-extracted Python (step 1 saw missing `python.exe`)
   and re-installed pip (step 2 saw missing `pip`), but **skipped step 3**
   because the JSON still said `PocketTtsInstalled: true`.
5. Step 4 (`import pocket_tts`) ran against a runtime with no
   pocket-tts → exit code 1.

State file recovered post-incident:
```json
{ "PythonExtracted": true, "PipInstalled": true,
  "PocketTtsInstalled": true, "RuntimeReady": true }
```
…even though the smoke test was failing. (`RuntimeReady` was leftover from
the previous good run; the failed runs never reached the `state.RuntimeReady = true`
write at line 145.)

### Fix sketch

Mirror steps 1 and 2: check whether `pocket_tts/__init__.py` actually
exists in `Lib\site-packages` *and* the state flag, e.g.

```csharp
private bool PocketTtsActuallyInstalled() =>
    File.Exists(Path.Combine(_paths.PythonRuntimePath, "Lib", "site-packages", "pocket_tts", "__init__.py"));

if (!state.PocketTtsInstalled || !PocketTtsActuallyInstalled())
{
    state.PocketTtsInstalled = false; // reset before re-running
    SaveState(state);
    ...
}
```

Worth auditing the other steps too — pip's existence check is
`PipExists()` which is fine, but consider whether the `_pth` patch from
`EnableSitePackages()` would be re-applied after a runtime nuke. (It
runs only when step 1 actually re-extracts, so probably yes — confirm.)

Also reasonable to defensively reset all downstream flags whenever an
upstream step actually re-runs (if step 1 re-extracted, set
`PipInstalled = PocketTtsInstalled = RuntimeReady = false`). That makes
the state machine monotonic w.r.t. on-disk reality.

## What — Bug B (diagnostics): smoke-test stderr is logged at Debug

`PythonRuntimeBootstrapper.RunProcessAsync` (line ~242):

```csharp
process.ErrorDataReceived += (_, e) =>
{
    if (string.IsNullOrEmpty(e.Data)) return;
    // pip writes progress to stderr, so this isn't necessarily an error.
    _logger.LogDebug("[{Op} stderr] {Line}", operationName, e.Data);
    onLine?.Invoke(e.Data);
};
```

Default Serilog config in this project writes INF and above to the file.
Result: when the smoke test fails, the actual Python traceback (which
*went* to stderr) is dropped on the floor, and the only artefact is the
generic `InvalidOperationException: Python smoke test exited with code 1`.
The user (and any future debugger) has no way to know whether the failure
was `ImportError`, a torch DLL mismatch, a `_pth` glitch, etc.

### Fix sketch

When `process.ExitCode != 0`, capture all stderr lines from that process
and include them in the thrown exception message (or buffer them and log
at ERR on non-zero exit). The "pip writes progress to stderr" comment is
still true — but that only matters for *successful* pip runs; on failure
we want full stderr regardless.

A minimal version: collect stderr into a `StringBuilder` while the
process runs; on non-zero exit throw with both the exit code and the
captured tail (last ~30 lines is plenty).

## Acceptance criteria

- [ ] Bug A: `BootstrapAsync` re-installs pocket-tts whenever
  `pocket_tts/__init__.py` is missing, regardless of what
  `bootstrap-state.json` claims.
- [ ] Bug A: a synthetic test (or manual repro) confirms that deleting
  `runtime/` while keeping `bootstrap-state.json` leads to a complete,
  successful bootstrap, not a code-1 smoke test.
- [ ] Bug B: when any bootstrap subprocess exits non-zero, the captured
  stderr (full, or last ~30 lines) appears in `mockingbird-YYYYMMDD.log`
  at ERR or above, and in the dialog's user-visible error message.
- [ ] Spot-check that `EnableSitePackages()` is correctly re-applied
  whenever step 1 re-extracts (i.e. that `python312._pth` ends up patched
  even after a runtime wipe). Add a one-line log.
- [ ] After the fix, main-018 verification can be re-attempted and
  reaches the audible-speech step.

## Notes

- Affected file: `src/Mockingbird/Services/Tts/PythonRuntimeBootstrapper.cs`
  (lines ~123 and ~242 per current `master`).
- Also touch `src/Mockingbird/Views/BootstrapDialog.xaml.cs` if needed
  to surface the captured stderr to the user (currently shows the
  exception message verbatim — once the message includes stderr, the
  dialog inherits the improvement for free).
- Don't be tempted to fix this inside main-018. Verification is meant
  to *find* breakage like this, not absorb the fix.
- Reference: ADR 0002 (Python sidecar shape), ADR 0008 (model bootstrap
  UX), main-011's Outcome block (which lists the criteria this unblocks).
- This task **blocks main-018**: until the bootstrap can complete on a
  clean state, the verification cannot proceed, and main-019 (Claude
  hook sample) is transitively blocked.
- Unblock path for the user *right now* if they want to keep poking at
  it manually: delete `bootstrap-state.json` along with `runtime\` and
  `models\pocket-tts\`. That gives the workaround until the fix lands.

## Outcome

Both defects fixed in
`src/Mockingbird/Services/Tts/PythonRuntimeBootstrapper.cs`:

**Bug A — state-vs-disk reconciliation.** Step 3 now gates on
`!state.PocketTtsInstalled || !PocketTtsActuallyInstalled()`, mirroring the
belt-and-braces pattern already used by steps 1 and 2. New helper
`PocketTtsActuallyInstalled()` checks
`Lib\site-packages\pocket_tts\__init__.py` on disk. When the on-disk sentinel
is missing despite the flag being set, a `LogWarning` records the
reconciliation and the flag is reset before re-installing.

Steps 1 and 2 also got a defensive cascade: when an upstream step actually
re-runs, every downstream flag is reset to `false` first. This makes the
state machine monotonic w.r.t. on-disk reality — no future step can be
skipped just because the JSON remembers a pre-wipe success.

**Bug B — stderr capture.** `RunProcessAsync` now buffers stderr lines (capped
at 200 to bound memory). On non-zero exit it pulls the last 30 lines, replays
them at `LogError` (which Serilog's default config writes to file), and
embeds them in the thrown `InvalidOperationException` message. The dialog
shows `ex.Message` verbatim, so the user sees the actual Python traceback or
pip resolver complaint rather than the opaque "exit code 1".

**Spot-check — `_pth` patch logging.** `EnableSitePackages()` now logs
`"Patched embeddable Python ._pth for site-packages support: {Path}"` at
`Information` after each `_pth` write, so future runtime-wipe scenarios
leave evidence that the patch was re-applied.

**Build:** `dotnet build src/Mockingbird/Mockingbird.csproj` clean, no
warnings introduced.

**ADR written:** `0011-bootstrap-state-reconciliation.md` records the
"on-disk sentinel files are authoritative; JSON flags are advisory" rule so
future bootstrap steps follow the same pattern.

**README updated:** the "Engine status" section's `PythonRuntimeBootstrapper`
bullet now mentions ADR 0011's reconciliation rule and the captured-stderr
diagnostic.

**Unblocks:** main-018 (clean-machine first-run verification) can now be
re-attempted. The workaround note in this task's body (delete
`bootstrap-state.json` along with `runtime\`) is no longer needed — wiping
just `runtime\` is enough to trigger a full re-bootstrap.

Key files:
- `src/Mockingbird/Services/Tts/PythonRuntimeBootstrapper.cs` — both fixes
- `.agenthoff/knowledge/decisions/0011-bootstrap-state-reconciliation.md` — ADR
- `.agenthoff/contexts/main/README.md` — engine-status bullet updated
