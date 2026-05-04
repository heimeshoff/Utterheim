# 0019. AppearanceMode persists in settings.json (not registry)

Date: 2026-05-04

## Status

Accepted — ratified by main-029 (2026-05-05).

## Context

mockingbird's Settings page (post main-029) gains a Light / Dark / System
appearance picker, modelled on WhisperHeim's General-page picker. The
selection needs to persist across launches.

ADR 0017 chose the **registry** as the source of truth for "Launch at
startup" because that flag is a Windows-shell concern (Run-key) that
external tools mutate. The same logic does **not** transfer to
appearance mode, but it would be wrong to assume that without writing
the reasoning down — a future maintainer looking at the two adjacent
toggles (one in registry, one in JSON) deserves a one-paragraph answer
to "why the asymmetry?"

Two storage choices, parallel to ADR 0017:

1. **`settings.json` (`appearanceMode: "Light" | "Dark" | "System"`).**
   Lives next to `defaultVoiceId`, `outputDeviceId`, `startMinimised`.
   Read once at startup by `EntryPoint` and fed into
   `ApplicationThemeManager.Apply(...)`; read on every Settings-page
   navigation by the picker to highlight the active tile.

2. **Registry** under e.g. `HKCU\Software\Mockingbird\Theme`.

## Decision

Persist `AppearanceMode` in `settings.json`. Schema:

```jsonc
{
  // ... existing fields ...
  "appearanceMode": "Light"
}
```

Backed by a typed `AppearanceMode` enum on `UserSettings`
(`Light | Dark | System`). Default value when the field is absent
(existing installs) is `Light`, matching the new app default declared
in main-029. The default is applied **in memory** — not persisted on
read — so the JSON file stays unchanged until the user explicitly
toggles the picker.

## Consequences

### Positive

- **Symmetric with the other in-app preferences.** `defaultVoiceId`,
  `outputDeviceId`, `startMinimised` all live in `settings.json` for
  the same reason: they are application-internal state with no
  external mutator.
- **No external surface to drift from.** Unlike the Run-key, there is
  no Windows control panel, msconfig view, or third-party uninstaller
  that touches `appearanceMode`. The "two sources of truth" failure
  mode that justified ADR 0017's registry choice does not exist here.
- **Portable.** Copying `settings.json` to a new machine carries the
  user's appearance preference along with their other in-app
  preferences. (Per ADR 0017's negative consequence: this is the
  desired behaviour for app-internal preferences and the
  *un*desired behaviour for deployment-shape preferences. Appearance
  is the former.)
- **Forward-compatible.** Unknown JSON fields are already ignored on
  read (per `UserSettings.ReadOptions`), and the enum serialises as a
  plain string so a future schema bump is trivial.

### Negative

- **String-based migration risk.** If we ever rename `"Light"` to
  `"light"` we have to handle the old casing on read. Mitigated by
  case-insensitive enum parsing.
- **Settings.json now describes UI presentation alongside functional
  state.** Minor — the file is already a heterogeneous bag of
  preferences, and a user editing it by hand would not be confused by
  one more entry.

## Implementation

- `UserSettings.SettingsData` gains
  `[JsonPropertyName("appearanceMode")] public AppearanceMode AppearanceMode { get; set; } = AppearanceMode.Light;`
  with `JsonStringEnumConverter` registered on `ReadOptions` /
  `WriteOptions` so the value round-trips as `"Light"` / `"Dark"` /
  `"System"`.
- A public `AppearanceMode` property on `UserSettings` mirrors the
  setter pattern of `OutputDeviceId` / `StartMinimised`, including a
  `AppearanceModeChanged` event.
- `EntryPoint` (or wherever `App.OnStartup` runs before `MainWindow.Show`)
  resolves `UserSettings` from DI and calls
  `ApplicationThemeManager.Apply(...)` / `ApplySystemTheme()` once with
  the current value. Subsequent in-page toggles call the same
  `ApplicationThemeManager` API directly **and** assign
  `userSettings.AppearanceMode`, which persists.

## Verification

After toggling Dark in the picker, `settings.json` contains
`"appearanceMode": "Dark"`. After app restart, the window opens in
Dark theme without further user action. Deleting the
`appearanceMode` line from `settings.json` and relaunching restores
Light (the in-memory default) without the file being rewritten —
verified by checking the file's mtime.
