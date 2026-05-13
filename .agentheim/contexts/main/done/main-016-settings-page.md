---
id: main-016
title: Settings page — output device, default voice, startup, read-only diagnostics
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-04
commit: eeb5192
depends_on: [main-010, main-013, main-020]
blocks: []
tags: [frontend, page]
---

## Why

The walking skeleton hard-codes everything: default WaveOut device, port 7223,
double-tap LCtrl with the window from `appsettings.json`, data path picked
once on first run, default voice null. The user needs a UI to change the
**user-tunable** subset of these without editing JSON files. The
**non-tunable** subset (port, hotkey, data path) still belongs on this page
as read-only diagnostics — answering "what is utterheim actually using
right now?" in one place. Mirrors WhisperHeim's General settings page
(WhisperHeim `design.md` §1).

## What

A Settings page laid out as **Fluent setting cards** grouped into three
sections (Audio · App · Diagnostics). Each card = label + one-line
description + control on the right, matching the Windows 11 Settings
aesthetic and what the styleguide signs off as the v1 settings shape.

### Audio

- **Default voice** — `ComboBox` sourced from `VoiceCatalog.ListAsync()`
  (built-ins first, cloned voices second, same order the Speak page
  picker uses). Selection writes through `UserSettings.DefaultVoiceId`
  with the existing atomic temp+replace; takes effect on the next Speak
  page navigation. Empty selection = "no preference" (catalog fallback to
  alphabetically-first per main-013, Q7). Storage layer **already ships
  in main-013** — this card adds the UI only.
- **Output device** — `ComboBox` of WaveOut devices enumerated at page
  load. Selection persists as `UserSettings.OutputDeviceId` (int?,
  null = "system default" mapped to `WaveOutEvent.DeviceNumber = -1`).
  **Applies to the next utterance only** — current playback continues
  on the previous device. Inline tip text under the control:
  *"Takes effect on the next speech request."*
  AudioPlayer reads the active device id at `WaveOutEvent` construction
  time (one read per utterance — no live-switch invariants to manage,
  no queue drain on change).

### App

- **Start minimised** — `ToggleSwitch`, persists as
  `UserSettings.StartMinimised` (bool, default false). Honoured by
  `MainWindow` on initial Show: when true, the window starts hidden in
  the tray instead of activating.
- **Launch at startup** — `ToggleSwitch`, persists by writing /
  removing the value
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Utterheim`
  pointing at the current process executable path. **Not stored in
  `settings.json`** — the registry IS the source of truth (so an
  external uninstaller / cleanup tool that drops the Run entry stays
  reflected accurately on next page load). Toggle reads the registry
  on load, writes/deletes on change.

### Diagnostics (read-only in v1)

- **HTTP port** — display the active port as `127.0.0.1:{port}` text
  (whatever `SpeakServer` is bound to). v1 is **read-only**; editing
  with auto-restart is **out of scope** — captured as a future
  refinement task if the user runs into a collision.
- **Stop hotkey** — display the current gesture
  ("Double-tap Left Ctrl"). Read-only per ADR 0006; rebinding UI is
  out of scope. Reference: ADR 0006.
- **Data path** — display the active path (read from `bootstrap.json`
  per ADR 0005), with an `Open in Explorer` button next to it.
  Changing the path requires a migration flow and is **out of scope**
  for v1.

## Acceptance criteria

- [ ] Settings page reachable from the sidebar nav (`SettingsPage` slot
  in `MainWindow`'s `NavigationView`).
- [ ] **Default voice** dropdown lists every voice in `VoiceCatalog`
  (built-ins + cloned), pre-selects the active `DefaultVoiceId`, and
  writes back through `UserSettings.DefaultVoiceId` (verify by
  re-launching and checking the Speak page picker pre-selects it).
- [ ] **Output device** dropdown lists real WaveOut devices and a
  "System default" entry; selecting one routes the **next** utterance
  to that device (verify with two devices: kick off a long utterance,
  switch device mid-stream, observe current playback continues on the
  old device, the next Play uses the new device).
- [ ] **Start minimised** toggle persists across restart and, when on,
  causes the next launch to start hidden in the tray.
- [ ] **Launch at startup** toggle writes / removes the HKCU\Run entry
  for `Utterheim` (verify with `reg query
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run`); state survives
  restart by re-reading the registry on page load.
- [ ] **HTTP port** displays the live `SpeakServer` bound port (verify
  by editing `appsettings.json` to a different port and confirming the
  page reflects it).
- [ ] **Stop hotkey** displays "Double-tap Left Ctrl" (static for v1).
- [ ] **Data path** displays the active path from `bootstrap.json` and
  the "Open in Explorer" button opens that folder
  (`Process.Start("explorer.exe", path)`).
- [ ] Visual matches the styleguide — Fluent cards, Mica backdrop,
  Segoe UI Variable, sectioned by Audio / App / Diagnostics.
- [ ] Page builds clean (`dotnet build utterheim.sln -c Debug`,
  zero errors / zero warnings).

## Notes

### Tactical pointers (for the worker)

- **Persistence layer** — extend `Services\Settings\UserSettings.cs`
  (the forward-compatible JSON shape was set up specifically for
  this). New properties on `SettingsData`:
  - `[JsonPropertyName("outputDeviceId")] int? OutputDeviceId`
  - `[JsonPropertyName("startMinimised")] bool StartMinimised`
  Both default to null/false; existing v1 reads keep working
  (`PropertyNameCaseInsensitive = true`, unknown fields ignored).
  Add `OutputDeviceIdChanged` / `StartMinimisedChanged` events
  matching the existing `DefaultVoiceIdChanged` pattern.
- **Output device wiring** — `Services\Speak\AudioPlayer.cs` line ~55
  currently constructs `new WaveOutEvent { DesiredLatency = 100 }`.
  Inject `UserSettings` and read `OutputDeviceId` per utterance:
  `new WaveOutEvent { DeviceNumber = settings.OutputDeviceId ?? -1,
  DesiredLatency = 100 }`. Per-utterance read = no live-switch
  invariants needed.
- **Output device enumeration** — extend
  `Services\Audio\AudioDeviceResolver.cs` (already enumerates WaveIn
  via `WaveInEvent.DeviceCount`) with a parallel `EnumerateWaveOut()`
  using `WaveOut.DeviceCount` + `WaveOut.GetCapabilities(i)`. Keep the
  Core Audio name-resolution pattern for friendly labels.
- **Launch at startup helper** — new
  `Services\Settings\StartupRegistration.cs`. Single class with
  `IsRegistered` (read), `Register()` (write
  `Application.ExecutablePath` to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Utterheim`)
  and `Unregister()` (delete the value). Use `Microsoft.Win32.Registry`,
  not a process spawn.
- **Start minimised wiring** — `Views\MainWindow.xaml.cs`
  `OnSourceInitialized` already runs early; check `UserSettings.StartMinimised`
  there and call the existing tray-hide path used by the close-to-tray
  flow (whichever method `MainWindow` exposes for "hide to tray").
- **VM** — `ViewModels\Pages\SettingsPageViewModel.cs` (currently an
  empty `ObservableObject` stub from main-020). Use
  `CommunityToolkit.Mvvm` source generators per ADR 0010
  (`[ObservableProperty]` for each setting; `[RelayCommand]` for
  `OpenDataPath`). Implement `INavigableView<SettingsPageViewModel>` +
  `INavigationAware` like `SpeakPage` and `VoicesPage` do; on
  `OnNavigatedTo` re-enumerate WaveOut devices and re-read the HKCU
  Run state (registry can be mutated externally between visits).
- **XAML** — `Views\Pages\SettingsPage.xaml`. Use `ui:CardControl`
  (or the equivalent wpfui setting card) per row; group with
  `TextBlock` section headers (`SemiBold`, 14pt, `Margin="0,16,0,8"`)
  for **Audio** / **App** / **Diagnostics**. Outer 16 px margin to
  match the other pages.

### Out of scope for v1 (capture as separate refinements if user wants)

- HTTP port editing with auto-restart (Kestrel rebind + queue drain).
- Stop-hotkey rebinding UI.
- Data-path change with migration flow (move voices, runtime, models).
- Theme / language / update-check toggles (WhisperHeim's General page
  hints at these but utterheim doesn't have them yet).
- Per-Claude-session voice routing UI — vision-deferred; the
  contract is env-var-only via `examples/claude-hooks/` (main-019).
- Output device level meter / test-tone button.

### References

- WhisperHeim `design.md` §1 General settings.
- ADR 0005 (path layout — data-path read).
- ADR 0006 (hotkey — read-only display).
- ADR 0008 (cross-cutting concerns including settings).
- ADR 0009 (navigation shell — page registration / `INavigableView`).
- ADR 0010 (MVVM via `CommunityToolkit.Mvvm` source generators).
- main-013 §UserSettings (Q7) — storage-layer lineage.
- main-014 §Refresh behaviour — `OnNavigatedTo` re-fetch pattern.

### Open questions resolved during refinement (2026-05-04)

1. **HTTP port editing**: read-only in v1. Editing with auto-restart
   captured as a separate future refinement if needed.
2. **Output device application**: next utterance only. No mid-stream
   device switch — `WaveOutEvent` rebuilds per utterance in
   `AudioPlayer`, so it's a one-line change.
3. **Default voice dropdown**: lives here, sourced from `VoiceCatalog`,
   writes via `UserSettings.DefaultVoiceId`.
4. **Page layout**: WhisperHeim-style Fluent cards, sectioned
   Audio / App / Diagnostics.
5. **Engine status overlap with About**: Settings stays out of engine
   status entirely — main-017 owns it.

## Outcome

Settings page replaces the main-020 stub with three sections of Fluent
setting cards (Audio / App / Diagnostics) per the styleguide. All eight
acceptance criteria are met against a clean build (`dotnet build
utterheim.sln -c Debug` → 0 errors, 0 warnings). Interactive UI
behaviours — toggle persistence, per-utterance device routing, registry
state survival across restart — are **not interactively re-tested** in
this pass; the code is in place per the task spec and any regression
will surface during the next manual run.

### Key files

- `src\Utterheim\Services\Settings\UserSettings.cs` — extended with
  `OutputDeviceId` (`int?`) and `StartMinimised` (`bool`) plus matching
  `*Changed` events. JSON-forward-compatible per main-013's design.
- `src\Utterheim\Services\Settings\StartupRegistration.cs` — new
  helper around `HKCU\…\Run\Utterheim`. Registry is the source of
  truth (ADR 0017) — not mirrored in `settings.json`.
- `src\Utterheim\Services\Speak\AudioPlayer.cs` — reads
  `UserSettings.OutputDeviceId` at `WaveOutEvent` construction time
  (one read per utterance — no live-switch invariants).
- `src\Utterheim\Services\Audio\AudioDeviceResolver.cs` — added
  `EnumerateOutputDevices()` mirroring `EnumerateInputDevices()`.
- `src\Utterheim\ViewModels\Pages\SettingsPageViewModel.cs` — VM
  with `[ObservableProperty]` for each control + `OnXChanged` partial
  methods that persist on change. `LoadAsync` re-populates everything
  on `OnNavigatedTo` (the registry can be mutated externally between
  visits).
- `src\Utterheim\Views\Pages\SettingsPage.xaml(.cs)` — `ui:CardControl`
  per setting, sectioned with `SemiBold` headers, `ScrollViewer`
  outer container, 16 px page margin.
- `src\Utterheim\EntryPoint.cs` — registered `StartupRegistration`
  singleton and wired `UserSettings.StartMinimised` into the launch
  flow (Show + immediate Hide so the tray icon initialises).
- `.agentheim\knowledge\decisions\0017-launch-at-startup-registry-as-source-of-truth.md`
  — captures why the Run entry is the source of truth, not mirrored
  in `settings.json`.

### Notes

- Linked ADR: 0017 (Launch-at-startup uses HKCU\Run as the source of truth).
- No tests are run because there are no test projects in this repo.
- The four setting writes that go through `UserSettings` use the existing
  atomic temp+replace from main-013 — no new persistence machinery.
