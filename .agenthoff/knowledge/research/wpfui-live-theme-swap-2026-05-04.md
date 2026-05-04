# wpfui live theme swap — API verification

Date: 2026-05-04
For: main-029 refinement (open question Q1 — appearance picker live-swap mechanism)

## Question

Mockingbird's Settings page (post main-029) needs a Light / Dark / System
appearance picker that swaps the active theme **at runtime** without an
app restart. What is the canonical wpfui (lepoco/wpfui) API for that, and
does WhisperHeim demonstrate it from a `Wpf.Ui` shell?

## Answer (locked)

Use `Wpf.Ui.Appearance.ApplicationThemeManager`:

```csharp
using Wpf.Ui.Appearance;

// Explicit Light or Dark
ApplicationThemeManager.Apply(ApplicationTheme.Light);
ApplicationThemeManager.Apply(ApplicationTheme.Dark);

// Follow system theme (reads Windows personalisation registry)
ApplicationThemeManager.ApplySystemTheme();
```

This is the current entry point in wpfui ≥ 3.0 (the version Mockingbird
already pulls in via the `ui:ThemesDictionary` / `ui:ControlsDictionary`
merged dictionaries in `App.xaml`). Older `Wpf.Ui.Appearance.Theme.Apply`
and `ThemeManager.Apply` paths are deprecated.

`Apply` swaps the merged ResourceDictionary in-process. All wpfui controls
and any user XAML using `{DynamicResource ...}` for theme brushes
(`ApplicationBackgroundBrush`, `TextFillColorPrimaryBrush`,
`CardBackgroundFillColorDefaultBrush`, etc.) re-render automatically.
`{StaticResource ...}` lookups do **not** refresh — note for our brand
brushes, which are theme-independent fixed hex values and so can stay
`StaticResource` safely.

## How WhisperHeim demonstrates it

`tooling\WhisperHeim\src\WhisperHeim\Views\Pages\GeneralPage.xaml.cs`
lines 79-106:

- Three click handlers (`ThemeLight_Click`, `ThemeDark_Click`,
  `ThemeSystem_Click`) each call a shared `ApplyTheme(string)`.
- `ApplyTheme` writes `_settingsService.Current.General.Theme = "Light"
  | "Dark" | "System"` (string), persists via `_settingsService.Save()`,
  then dispatches:
  - `"Light"` → `ApplicationThemeManager.Apply(ApplicationTheme.Light)`
  - `"Dark"`  → `ApplicationThemeManager.Apply(ApplicationTheme.Dark)`
  - `"System"`→ `ApplicationThemeManager.ApplySystemTheme()`
- After applying, `HighlightActiveTheme()` rebrushes the three tile
  backgrounds (transparent vs. `#19005FAA` highlight) so the picker UI
  reflects the active mode.

WhisperHeim persists the choice as the **string** `"Light" | "Dark" |
"System"`, not an enum, and re-applies it on every page Loaded via
`RefreshFromSettings → HighlightActiveTheme`. There is no startup
re-application path inside the page itself — WhisperHeim's app-startup
shell calls `ApplicationThemeManager.Apply(...)` once before the main
window shows, using the persisted string. (Mockingbird's `EntryPoint`
will need the equivalent — see main-029 Acceptance criteria.)

## Decision for Mockingbird

- **Picker mechanism**: identical to WhisperHeim — three tiles with
  click handlers calling `ApplicationThemeManager.Apply` /
  `ApplySystemTheme`.
- **Persistence**: a new `UserSettings.AppearanceMode` field of type
  `AppearanceMode` enum (`Light | Dark | System`) — typed enum rather
  than string because we own the schema and want compile-time safety;
  serialised as the JSON string by default for forward-compatibility
  with hand edits to `settings.json`.
- **Startup application**: `EntryPoint` (host startup, before
  `MainWindow.Show()`) reads `UserSettings.AppearanceMode` and calls
  `ApplicationThemeManager.Apply(...)` / `ApplySystemTheme()` once.
  This is the path that addresses existing installs which have no
  `appearanceMode` field — defaulting to `Light` in memory so the
  startup call applies Light visibly.
- **Live swap on toggle**: from the Settings page, the tile click
  handlers call `Apply` immediately, then write
  `UserSettings.AppearanceMode = ...`, then update the highlight
  swatch. No restart prompt.

## What we are not doing

- Not using the wpfui `WindowBackdrop` API for backdrop swapping —
  Mica stays Mica regardless of Light/Dark, which matches WhisperHeim.
- Not registering for system theme change events — `ApplySystemTheme`
  internally subscribes when called, so System mode tracks the OS
  setting going forward without us doing anything extra. Verified in
  WhisperHeim: their System tile only calls `ApplySystemTheme` once
  and the live OS toggle still updates the running app.

## References

- `tooling\WhisperHeim\src\WhisperHeim\Views\Pages\GeneralPage.xaml`
  lines 187-246 (the picker XAML)
- `tooling\WhisperHeim\src\WhisperHeim\Views\Pages\GeneralPage.xaml.cs`
  lines 79-106 (the click handlers + Apply pattern)
- wpfui source: `Wpf.Ui.Appearance.ApplicationThemeManager` (NuGet
  package `WPF-UI`).
