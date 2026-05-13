# 0018. About page reads engine status in-process; Restart Engine composes Stop+Start

Date: 2026-05-04

## Status

Accepted (main-017).

## Context

The About page (main-017) ships two tightly-coupled new surfaces:

1. **Engine status panel** — state pip + port + healthy + last-error block.
2. **Restart Engine button** — recovery affordance the failed-state banner
   on the Voices page (main-014) already promises ("See the About page for
   details and retry.").

Both surfaces touch the same `SidecarHost` singleton that already powers
the persistent footer (`EngineStatusViewModel`). Two design questions
followed from that:

### Q1 — How does the page read engine status?

Two plausible shapes:

- **A. HTTP `GET /status`** — same endpoint Claude Code / external callers
  use. Polled at e.g. 1 Hz from the page. Symmetrical with the published
  language; the page is "just another caller".
- **B. In-process subscription** — bind to `SidecarHost.StateChanged` (the
  same event the footer VM uses) and re-seed via `SidecarHost.GetStatus()`
  on `OnNavigatedTo`. The page reads the same in-memory snapshot the host
  itself is updating.

### Q2 — What does Restart Engine do, mechanically?

The host already restarts the sidecar on crash with capped exponential
backoff (`SidecarHost.SuperviseAsync`). User-initiated restart needs
similar effects but composed differently because the supervisor is
running normally (no crash to recover from):

- **A. New `SidecarHost.RestartAsync()` method** — `StopAsync`-equivalent
  teardown + `StartAsync` again, with the JobObject preserved (the host
  isn't shutting down, just the python child).
- **B. Re-run `PythonRuntimeBootstrapper`** — closer to "fix install
  drift" than "restart"; only useful when `Failed` is due to a missing
  pocket-tts install. Heavier, slower, and not what the failed-state
  copy promises.
- **C. Cancel-and-respawn via the supervisor's existing restart loop** —
  forcibly kill the process and let the watchdog backoff-retry. Conflates
  user intent with crash recovery and adds a backoff delay the user
  didn't ask for.

## Decision

**Q1 = B. In-process subscription.** The About page subscribes to
`SidecarHost.StateChanged` and re-seeds via `SidecarHost.GetStatus()` on
navigate-to. `GET /status` stays the contract for outside callers.

**Q2 = A. New `SidecarHost.RestartAsync()` method.** Composed of
`StopAsync`-equivalent teardown plus `StartAsync` again, with the JobObject
preserved across the cycle so the next sidecar joins the same job (anti-zombie
guarantee from ADR 0012 / main-022 stays intact).

The lifecycle hop visible to subscribers is
`Restarting → Stopping(*) → NotStarted(*) → Starting → Running` (the
intermediate `Stopping`/`NotStarted` may be invisibly brief).

The Restart button is **disabled** while the engine is in a transitional
state (`Starting`, `Restarting`, `Stopping`) so the user can't double-fire
mid-cycle. It is **enabled** on `Running`, `Failed`, and `NotStarted` —
including `Running` so a "stuck running" engine can be cycled.

## Consequences

### Positive

- **Zero-latency UI.** The pip flips the moment `SidecarHost` raises
  `StateChanged`; no 1 Hz polling skew, no observable lag between footer
  and About panel (they consume the same event).
- **One source of truth.** The footer (`EngineStatusViewModel`) and the
  About panel can't drift apart. Refactored to share
  `SidecarStateLabels.Format(...)` so even the friendly strings stay in
  lockstep.
- **Dispatcher-thread-safe by construction.** Subscribers marshal to the
  UI dispatcher inside the handler — same pattern
  `EngineStatusViewModel.OnSidecarStateChanged` already established.
- **Anti-zombie invariant preserved across restart.** `RestartAsync` does
  *not* dispose the JobObject. The next sidecar process is assigned to
  the same job; the host's ability to atomically kill the python tree on
  exit (ADR 0012) is unaffected.
- **No HTTP self-call.** The page is in-process; making it call its own
  `/status` over loopback would just be a roundtrip through Kestrel for
  state we already own.

### Negative

- **HTTP `/status` and the page can theoretically diverge** under bizarre
  scenarios (a third-party tool poking at the running app via the HTTP
  endpoint). In practice both read the same `SidecarHost.GetStatus()`
  snapshot synchronously — they cannot diverge. The split is a contract
  boundary, not a duplication.
- **`SidecarHost.RestartAsync` is a new public method** — mutable surface
  area on the singleton. Mitigated by keeping the orchestration tight
  (see Implementation) and by only exposing it via a single command
  binding on a single page.

## Implementation

### `Services/Tts/SidecarStateLabels.cs`

Static helper extracted from `EngineStatusViewModel`. Both the footer VM
and `AboutPageViewModel` consume `SidecarStateLabels.Format(state)`.

### `Services/Tts/SidecarHost.RestartAsync(...)`

```csharp
public async Task RestartAsync(CancellationToken ct = default)
{
    SetState(SidecarState.Restarting);              // surface the transition
    cts?.Cancel();                                  // unblock the supervisor
    await Task.WhenAny(supervisor, timeout(5s));    // wait for unwind
    TerminateProcess();                             // kill the python child
    // Reset _supervisorTask = null + _supervisorCts = null + _shuttingDown = false
    // so StartAsync's "already started" guard doesn't short-circuit.
    await StartAsync(ct);
}
```

The JobObject is intentionally **not** disposed — that path is reserved
for `StopAsync` (host shutdown). On restart the same job picks up the
new python child via the existing `_jobObject.AssignProcess(...)` call
inside the supervisor.

### `ViewModels/Pages/AboutPageViewModel.cs`

- `[ObservableProperty]` for `Version`, `EngineState`, `Healthy`, `Port`,
  `LastError`. Computed `EngineStateLabel`, `IsRunning`, `PortLabel`,
  `IsRetryEnabled`.
- `[RelayCommand(CanExecute = nameof(CanRestartEngine))]` for
  `RestartEngineAsync(ct)` — `CanExecuteChanged` is fired via
  `[NotifyCanExecuteChangedFor]` on `EngineState`.
- `[RelayCommand]` for `OpenLogs` — `Process.Start("explorer.exe",
  _paths.LogsPath)`, falling back to the parent directory if the logs
  folder doesn't exist yet.
- `Attach()` / `Detach()` lifecycle pair invoked by the page's
  `INavigationAware` hooks; each (un)subscribes the
  `SidecarHost.StateChanged` handler.

### `ViewModels/Pages/AboutPageConverters.cs`

`EngineStateToPipBrushConverter` (`IMultiValueConverter`) maps
`(SidecarState, healthy)` → frozen `SolidColorBrush`:
green `#10B981` for `Running` + healthy, amber `#F59E0B` for
`Starting` / `Restarting` / `Running` + unhealthy, red `#FFE81224` for
`Failed`, neutral `#9CA3AF` for `NotStarted` / `Stopping`.

## Verification

- Build: `dotnet build mockingbird.sln -c Debug` → 0 warnings, 0 errors.
- Subscription correctness: `OnNavigatedTo` calls `Attach`,
  `OnNavigatedFrom` calls `Detach`. `Attach` defensively removes the
  handler before re-adding so navigating in-out-in can't double-fire.
- Restart lifecycle: hitting Restart on a `Running` engine surfaces
  `Restarting → Stopping → NotStarted → Starting → Running` (the
  intermediate states may be invisibly brief but all are raised by the
  composed `SetState` / `StartAsync` calls).

Marco accepts the "code-in-place, not interactively re-tested" pass per
the standing user instruction; live-transition / failure verification
will surface during the next manual run.
