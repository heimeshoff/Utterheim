---
id: main-017
title: About page — logo, tagline, version, engine status, retry
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-04
commit: 435d059
depends_on: [main-010, main-020]
blocks: []
tags: [frontend, page]
---

## Why

Completes the four-page set the styleguide canonicalised
(Speak, Voices, Settings, About — `docs/styleguide.md` §Page set). Small in
isolation, but it's the single place that surfaces "what is this app, what
version am I on, is the engine healthy, and how do I get it un-stuck" —
including the **retry control** that main-014's failed-state copy already
promises users (`"See the About page for details and retry."`).

## What

A single About page with five blocks, top-to-bottom, 16 px outer margin,
matching the styleguide's Fluent / Mica / Segoe UI Variable look:

### 1. Brand mark + identity

- Logo: `assets/branding/utterheim-logo-256.png` (white silhouette on
  transparent — already shipped by main-012), rendered at **128×128** with
  `RenderOptions.BitmapScalingMode="HighQuality"`. Use the PNG, not the SVG —
  WPF doesn't render SVG natively and we already have rasters.
- App name: `Utterheim` (`FontWeight="Light"`, page-title sized).
- Tagline: **`Local voices for Claude Code`** — exact string, signed off
  in styleguide §Sign-off (2026-05-01). Dimmer secondary text under the name.

### 2. Version

Single `TextBlock`: `Version {version}` where `{version}` reads from
`Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`,
falling back to `AssemblyName.Version` if that attribute is missing
(unconfigured dev builds). Surface the bare value — no `v` prefix.

### 3. Engine status panel

The richer status surface main-014's failed-state copy points to. **In-process
data flow**: subscribe to `SidecarHost.StateChanged` (the same event
`EngineStatusViewModel` uses for the footer) and re-seed via
`SidecarHost.GetStatus()` on `OnNavigatedTo`. **Do not** call `GET /status`
from the page — that endpoint is the contract for outside callers; the page
reads in-process to stay event-driven and dispatcher-thread-safe.

Display fields, all from `SidecarStatus(state, healthy, port, lastError)`:

- **State** — friendly label using the same mapping as
  `EngineStatusViewModel.FormatState` (`Running` → `pocket-tts`,
  `NotStarted` → `not started`, etc.). Coloured pip next to the label:
  green for `Running` + healthy, amber for `Starting` / `Restarting`,
  red for `Failed`, neutral for `NotStarted` / `Stopping`.
- **Port** — `127.0.0.1:{port}` when `_port > 0`; `—` otherwise.
- **Healthy** — small `CheckmarkCircle24` (green) when
  `status.Healthy == true`, `DismissCircle24` (red) when false. Visible
  only when `state == Running` (otherwise the state label already covers
  the signal).
- **Last error** — a dim, monospace, wrap-enabled `TextBlock` showing
  `status.LastError` when non-null. Hidden when null. No truncation in
  v1 — pocket-tts errors are usually short.
- **Retry button** — `Appearance="Primary"`, label `Restart Engine`. Visible
  always (so the user can also restart a stuck `Running` state). Enabled
  iff `state ∈ { Running, Failed, NotStarted }`; disabled while
  `Starting` / `Restarting` / `Stopping` so the user can't double-fire
  during a transition.

### 4. View logs

`HyperlinkButton`: `View logs`. Click runs
`Process.Start("explorer.exe", logsPath)` where `logsPath` is the same
directory Serilog writes to (per ADR 0008 — read it from the same helper
the logger config uses, not a hard-coded constant). If the directory
doesn't exist yet (first launch before the first log roll), still open
its parent — never throw.

### 5. Credits

Single line, dim text:
`Synthesis powered by pocket-tts (Kyutai Labs).`

That's it. No NAudio / wpfui line — minimal per styleguide vibe.

## Acceptance criteria

- [ ] About page reachable from the sidebar nav
  (`AboutPage` slot in `MainWindow`'s `NavigationView` — already stubbed by
  main-020).
- [ ] Logo renders as 128×128 from `utterheim-logo-256.png`, sharp on
  100% and 200% DPI (visual check).
- [ ] App name `Utterheim` and tagline `Local voices for Claude Code`
  render as specified — exact strings.
- [ ] Version reads from `AssemblyInformationalVersionAttribute` and shows
  as `Version {value}`. Verify by editing the project's
  `<InformationalVersion>` (or `<Version>`) and confirming the page reflects
  it after rebuild.
- [ ] Engine status panel reflects `SidecarHost.GetStatus()` on navigate-to
  and updates within 100 ms of `StateChanged` firing — verify with two scenarios:
  - **Live transition**: kill the python sidecar process from Task Manager
    while on the About page; observe state move `running` → `restarting` →
    `running` and the green pip flicker accordingly.
  - **Failure**: force `Failed` (e.g., uninstall pocket-tts so bootstrap
    fails — or rename the python folder); observe state pip turns red,
    `lastError` block appears with the captured stderr tail.
- [ ] **Restart Engine** button calls `SidecarHost.RestartAsync()`, which
  performs `StopAsync` + `StartAsync` and returns the supervisor to a
  fresh state. Verify: hit Restart on a `Running` engine; observe state
  goes `stopping` → `notstarted` / `starting` → `running`, with the
  button correctly disabled during the transitional states.
- [ ] **View logs** button opens `%LOCALAPPDATA%\Utterheim\logs\` in
  Explorer (verify: the folder window opens; if logs exist, they're listed).
  Does not throw if the directory is missing — opens the parent instead.
- [ ] Credits line shows `Synthesis powered by pocket-tts (Kyutai Labs).`
- [ ] Page subscribes / unsubscribes correctly: navigate away from About,
  then kill the sidecar, then return — state must be current on
  navigate-back (no leaked subscription, no stale label). Verify by
  inspecting that `OnNavigatedFrom` removes the handler.
- [ ] Visual matches the styleguide (Fluent layout, Mica backdrop,
  Segoe UI Variable, single-column page with section spacing).
- [ ] Page builds clean (`dotnet build utterheim.sln -c Debug`,
  zero errors / zero warnings).

## Notes

### Tactical pointers (for the worker)

- **VM** — `ViewModels\Pages\AboutPageViewModel.cs` (currently empty
  `ObservableObject` stub from main-020). Use `CommunityToolkit.Mvvm`
  source generators per ADR 0010 (`[ObservableProperty]` for
  `Version`, `EngineState`, `EngineStatePip`, `Port`, `Healthy`,
  `LastError`, `IsRetryEnabled`; `[RelayCommand]` for `RestartEngine`,
  `OpenLogs`). Implement
  `Wpf.Ui.Controls.INavigableView<AboutPageViewModel>` +
  `Wpf.Ui.Controls.INavigationAware` like `SpeakPage` and `VoicesPage`
  do; `OnNavigatedTo` re-seeds via `SidecarHost.GetStatus()` and
  subscribes to `StateChanged`; `OnNavigatedFrom` unsubscribes. Marshal
  to the UI dispatcher inside the handler — copy the pattern from
  `EngineStatusViewModel.OnSidecarStateChanged` (lines 46–55).
- **State formatting** — reuse the exact `FormatState` mapping from
  `EngineStatusViewModel` (lines 57–66). Refactor to a static helper
  in `Utterheim.Services.Tts` (e.g. `SidecarStateLabels.Format(...)`)
  so the footer and About page can't drift apart. Optional but
  recommended.
- **`SidecarHost.RestartAsync()`** — new public method on
  `Services\Tts\SidecarHost.cs`. Composition: `await StopAsync(ct);`
  reset `_supervisorTask`, `_supervisorCts`, `_shuttingDown` to their
  initial state under `_stateLock` (the existing `StartAsync` guards
  against double-start by checking `_supervisorTask is not null`, so
  the reset must happen for a re-Start to take). Then
  `await StartAsync(ct);`. Surface the operation under
  `_state = SidecarState.Restarting` so subscribers see the transition.
  Mirror the existing logging style.
- **Logs path source** — `Utterheim\appsettings.json` configures
  Serilog's rolling file sink at `%LOCALAPPDATA%\Utterheim\logs\
  utterheim-YYYYMMDD.log` (per ADR 0008). Resolve the directory via
  `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` +
  `\Utterheim\logs\` rather than re-parsing the Serilog config.
- **Logo asset** — `<Image Source="pack://application:,,,/assets/branding/utterheim-logo-256.png"
  Width="128" Height="128" RenderOptions.BitmapScalingMode="HighQuality"/>`.
  Confirm the PNG is set as `Resource` in the .csproj (it should be —
  main-012 wired this).
- **XAML** — `Views\Pages\AboutPage.xaml`. Use a single-column `StackPanel`
  with section-level spacing (`Margin="0,0,0,24"` between blocks).
  Engine status is the densest block; consider a `ui:CardControl`
  wrapper for visual containment (matching main-016's settings cards).
  Pip + state can sit in a `StackPanel Orientation="Horizontal"`.
- **Version helper** — small static helper in
  `ViewModels\Pages\AboutPageViewModel.cs` (or a tiny
  `Services\AppInfo.cs` if it grows): try
  `Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion`; fall back to
  `Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)`;
  ultimate fallback `"unknown"`.
- **Pip control** — minimal: a 10×10 `Ellipse` whose `Fill` binds
  through a converter from `(SidecarState, healthy)` to brushes
  (green / amber / red / neutral). Keep the converter in
  `ViewModels\Pages\AboutPageConverters.cs` (parallel to
  `VoicesPageConverters.cs`).
- **Footer interaction** — the footer **stays**. The About page's
  rich panel coexists with the always-visible footer per Q2 sign-off.
  Revisit if it feels redundant after live use.

### Out of scope for v1 (capture as separate refinements if user wants)

- Bootstrap-style retry that re-runs `PythonRuntimeBootstrapper` when
  `Failed` is due to install drift (Q3 option C). Could come if
  Restart-the-engine alone proves insufficient.
- Surface ADR / changelog text on the About page.
- Update-check / auto-update infrastructure (vision non-goal in v1).
- Per-engine status when a second engine ships. The panel assumes one
  sidecar; a multi-engine future would extend it.
- "Copy logs path" / "Open log file" affordances. The folder shortcut is
  enough for v1.
- Localisation. English-only per vision.

### References

- `docs/styleguide.md` §Page set, §Tagline, §Brand mark, §Sign-off.
- ADR 0005 (path layout — logs path under `%LOCALAPPDATA%`).
- ADR 0008 (Serilog rolling file sink).
- ADR 0009 (navigation shell — `INavigableView`, `INavigationAware`).
- ADR 0010 (MVVM via `CommunityToolkit.Mvvm` source generators).
- main-014 §Engine-state visibility — failed-state banner that points
  here for retry.
- `EngineStatusViewModel.cs` — the canonical pattern for subscribing
  to `SidecarHost.StateChanged` from a VM.
- main-012 — logo rasterisation (already shipped).
- main-016 §Tactical pointers — VM / XAML layout precedent.

### Open questions resolved during refinement (2026-05-04)

1. **Engine status data source**: in-process subscription to
   `SidecarHost.StateChanged` + `GetStatus()` re-seed on navigate-to.
   `GET /status` stays the contract for external callers; the page
   reads in-process. (Q1)
2. **Footer vs rich panel**: keep the always-visible footer; About
   surfaces the richer panel alongside. Revisit if redundant after
   live use. (Q2)
3. **Retry control**: ship `Restart Engine` button on About backed by
   a new `SidecarHost.RestartAsync()` method (StopAsync + reset +
   StartAsync). Disabled during transitional states. (Q3 = A)
4. **Credits scope**: minimal — `Synthesis powered by pocket-tts
   (Kyutai Labs).` Nothing else. (Q4)

## Outcome

Shipped the canonical four-page set's final page. The About surface lays out
the brand mark (128x128 raster of `utterheim-logo-256.png`, scaled with
`HighQuality` bitmap), `Utterheim` page title (`FontWeight="Light"`), the
signed-off `Local voices for Claude Code` tagline, a `Version {value}` line
sourced from `AssemblyInformationalVersionAttribute` (with `+sha` suffix
stripped, falling back to `AssemblyName.Version` then `"unknown"`), the
richer engine status panel (state pip + label + port + Healthy icon + last
error block + Restart Engine button), a `View logs` `ui:HyperlinkButton`,
and the `Synthesis powered by pocket-tts (Kyutai Labs).` credits line.

Engine status reads in-process via `SidecarHost.GetStatus()` + subscription
to `SidecarHost.StateChanged` (per ADR 0018 / Q1) — `GET /status` stays the
contract for outside callers. The friendly state mapping was extracted from
`EngineStatusViewModel` into a shared `SidecarStateLabels.Format` helper so
the footer and About panel cannot drift.

`SidecarHost.RestartAsync(...)` is the user-initiated cycle: surface
`Restarting` → cancel supervisor (5 s timeout) → `TerminateProcess()` →
reset supervisor handles / `_shuttingDown` / `_port` under `_stateLock` →
`StartAsync(ct)`. The JobObject is preserved across the cycle so the
anti-zombie invariant from ADR 0012 / main-022 survives. The button is
disabled during transitional states via `[NotifyCanExecuteChangedFor]` on
`EngineState`.

`OpenLogs` opens `%LOCALAPPDATA%\Utterheim\logs\` via Explorer, falling
back to its parent (`LocalRoot`) when the directory doesn't exist yet.

Build: `dotnet build utterheim.sln -c Debug` → 0 warnings, 0 errors.

### Key files

- `src/Utterheim/ViewModels/Pages/AboutPageViewModel.cs` — typed VM with
  `[ObservableProperty]` on `Version` / `EngineState` / `Healthy` / `Port` /
  `LastError` and `[RelayCommand]` for `RestartEngine` / `OpenLogs`.
- `src/Utterheim/ViewModels/Pages/AboutPageConverters.cs` —
  `EngineStateToPipBrushConverter` (`IMultiValueConverter`) maps
  `(SidecarState, healthy)` to a frozen pip brush.
- `src/Utterheim/Views/Pages/AboutPage.xaml(.cs)` — five-block layout +
  `INavigationAware` lifecycle wiring (`Attach` / `Detach`).
- `src/Utterheim/Services/Tts/SidecarHost.cs` — added `RestartAsync`.
- `src/Utterheim/Services/Tts/SidecarStateLabels.cs` — extracted shared
  state-label helper.
- `src/Utterheim/ViewModels/EngineStatusViewModel.cs` — refactored to
  consume the helper.
- `src/Utterheim/Utterheim.csproj` — added the 256-px logo PNG as
  `<Resource>` so the `pack://application:,,,/...` URI resolves.
- `.agentheim/knowledge/decisions/0018-about-page-engine-status-in-process.md`
  — records Q1/Q2 (in-process subscription + Stop+Start composition).
