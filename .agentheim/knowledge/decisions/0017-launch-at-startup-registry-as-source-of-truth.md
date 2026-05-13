# 0017. Launch-at-startup uses HKCU\Run as the source of truth

Date: 2026-05-04

## Status

Accepted (main-016).

## Context

utterheim's Settings page (main-016) has a "Launch at startup" toggle. Two
plausible storage choices:

1. **Mirror state in `settings.json`** — a `launchAtStartup: bool` field
   alongside `defaultVoiceId`, `outputDeviceId`, `startMinimised`. On change,
   write both the JSON value AND the registry entry under
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Utterheim`.
2. **Registry-only** — the Run entry IS the state. The toggle reads the
   registry on every page navigation and writes/deletes on change.

Option (1) is the more obvious "all preferences in one place" shape and is
how a lot of installers keep things consistent. Option (2) trades that
neatness for a stronger invariant: there is exactly one place the user (or
an external tool) can change the answer, and the UI always reflects reality.

## Decision

The registry is the source of truth. We do **not** store
`launchAtStartup` in `settings.json`. The Settings page reads
`StartupRegistration.IsRegistered` on every `OnNavigatedTo` and writes /
deletes the value on toggle change. Registry value name is
`Utterheim` (matches the product name so it's identifiable in `regedit`
and Task Manager's Startup tab); the value command is the quoted current
executable path resolved via `Process.MainModule.FileName`.

## Consequences

### Positive

- **No drift between two sources.** A third-party uninstaller / cleanup
  tool that drops the Run entry stays reflected accurately on the next
  page load — the toggle simply re-reads and shows "off" without us
  needing a reconciliation pass.
- **No "settings.json says yes but Windows doesn't launch us" failure
  mode.** That bug class is closed by construction.
- **Symmetric with how Windows itself surfaces this** — Task Manager's
  Startup tab, `msconfig`, and Settings → Apps → Startup all read the
  same registry entry. We're not inventing a parallel store.

### Negative

- **One field on the Settings page is *not* persisted via
  `UserSettings`** — minor pedagogical irregularity. Mitigated by a
  comment on `StartupRegistration` explaining why and a comment on
  `SettingsPageViewModel.OnLaunchAtStartupChanged` pointing at the
  registry path.
- **Reading the registry every navigation is an I/O hit** — negligible
  in practice (sub-millisecond on a warm key), and only happens when
  the user opens Settings.
- **Cannot restore the flag by copying `settings.json` to a new
  machine.** Acceptable: per ADR 0005, `settings.json` is data-path
  preferences, not deployment-shape preferences. "Launch at startup"
  is a per-machine deployment concern.

## Implementation

`Services\Settings\StartupRegistration.cs` is a thin singleton wrapper
exposing `IsRegistered` (read), `Register()` (write quoted exe path),
and `Unregister()` (delete value). Consumes `Microsoft.Win32.Registry`
directly — no external dependencies.

The Settings page's `LaunchAtStartup` `[ObservableProperty]` is bound
two-way to a `ui:ToggleSwitch`; the `OnLaunchAtStartupChanged` partial
method invokes `Register()` / `Unregister()`. A `_suspendPersist` flag
on the VM prevents the partial method from firing while
`OnNavigatedTo` populates the toggle from the registry's current
state.

## Verification

`reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run` after
toggling on shows `Utterheim "<path-to>\utterheim.exe"`. Toggling
off removes the value. Closing and re-opening the Settings page
reflects the current registry state — including changes made by
external tools between visits.
