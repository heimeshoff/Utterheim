---
id: main-029
title: Adopt WhisperHeim styling wholesale — Light theme, brand palette, card spec, Appearance picker
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-05
commit:
depends_on: [main-010]
blocks: [main-030, main-032]
tags: [styleguide, theming, ui, settings, appearance]
---

## Why

The styleguide gate says mockingbird inherits WhisperHeim's design language. In
practice the current pages diverge in several visible ways:

1. **App.xaml uses `Theme="Dark"`**; WhisperHeim uses `Theme="Light"` (see
   [WhisperHeim App.xaml line 10](../../../../tooling/WhisperHeim/src/WhisperHeim/App.xaml)).
   The styleguide currently codifies the dark default — that decision was made
   before the user pressure-tested it visually, and it now reads as wrong:
   page titles like "Settings" and "Diagnostics" are too dark to read against
   the Mica backdrop.
2. **No brand palette is wired through.** WhisperHeim leans on `#FF25abfe`
   (cyan-blue, primary brand), `#FFff8b00` (orange, accent), and `#FF005FAA` /
   `#66005FAA` (deep blue used for section-header glyphs, version numbers,
   muted detail). Mockingbird's pages rely entirely on theme brushes — there's
   no brand colour in sight.
3. **Card style is inconsistent.** WhisperHeim cards are `Border` with
   `CardBackgroundFillColorDefaultBrush`, `CornerRadius=12`, `Padding=24`,
   uniform vertical spacing — see
   [WhisperHeim GeneralPage.xaml lines 60–101](../../../../tooling/WhisperHeim/src/WhisperHeim/Views/Pages/GeneralPage.xaml).
   Mockingbird Settings uses `ui:CardControl` with default padding and tight
   `Margin="0,0,0,8"` — visibly thinner spacing, no rounded-12 visual rhythm.
4. **Settings page has its own brighter background.** This is unintended —
   probably comes from `ui:CardControl`'s default chrome. WhisperHeim
   pages use `Background="{DynamicResource ApplicationBackgroundBrush}"` on the
   ScrollViewer and let Mica show through. Settings should match.
5. **No appearance picker.** WhisperHeim's General page has a Light / Dark /
   System scheme selector (see GeneralPage.xaml lines 187–246). The user wants
   the same in mockingbird's Settings.

## What

Re-align mockingbird's visual layer to WhisperHeim's actual design wholesale,
update the styleguide so the next task isn't fighting stale guidance, and add
the appearance picker.

### Theme + palette

- Switch `App.xaml` `ui:ThemesDictionary Theme` from `Dark` to `Light`.
- Define the four brand brushes once, **directly inside `App.xaml`'s
  `<Application.Resources>` `ResourceDictionary`** (alongside the existing
  `MergedDictionaries`, not inside them — wpfui's chain stays untouched).
  Declared as `SolidColorBrush` `StaticResource`s so they participate in
  static lookups from any Page:
  - `BrandPrimaryBrush` = `#FF25abfe` — primary brand (cyan-blue)
  - `BrandAccentBrush` = `#FFff8b00` — accent (orange)
  - `BrandDeepBrush` = `#FF005FAA` — section-header glyph colour, hyperlink
    accent on dark surfaces
  - `BrandDeepMutedBrush` = `#66005FAA` — version-tag and other supplementary
    numerals only (RGBA renders ≈ 2.2:1 contrast on Light backdrop; matches
    WhisperHeim verbatim, accepted as decorative not body-text per styleguide
    §Brand palette)

### Page chrome (applies to all four pages)

For Speak / Voices / Settings / About:

- Top-level `ScrollViewer` carries
  `Background="{DynamicResource ApplicationBackgroundBrush}"` and
  `VerticalScrollBarVisibility="Auto"`.
- Inner `StackPanel` uses `Margin="40,36,40,32"`, `MaxWidth="900"`,
  `HorizontalAlignment="Center"` — same as WhisperHeim.
- **Section headers**: `StackPanel Orientation="Horizontal"` with a
  `Margin="0,0,0,20"`, containing a `ui:SymbolIcon` (24-px,
  `Foreground="{StaticResource BrandDeepBrush}"`, `Margin="0,0,10,0"`)
  + a `TextBlock` `FontSize="10"` `FontWeight="Bold"` UPPERCASE label,
  `Foreground="{DynamicResource TextFillColorSecondaryBrush}"`,
  `VerticalAlignment="Center"`.
- **Cards**: `Border` `Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"`
  `CornerRadius="12"` `Padding="24"`. Spacing rule:
  - Between cards in the same section: `Margin="0,0,0,12"`.
  - On the **last** card of a section (before the next section header):
    `Margin="0,0,0,40"`.
- **Card label/description/control pattern** (the standard one-row card):
  Outer `<DockPanel>`. Left `<StackPanel DockPanel.Dock="Left"
  VerticalAlignment="Center" MaxWidth="400">` containing label
  (`FontSize="15"` `FontWeight="SemiBold"`
  `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`) and
  description (`FontSize="13"`
  `Foreground="{DynamicResource TextFillColorSecondaryBrush}"`
  `Margin="0,4,0,0"` `TextWrapping="Wrap"`). Right `DockPanel.Dock="Right"
  HorizontalAlignment="Right" VerticalAlignment="Center"` for the control
  (ToggleSwitch / ComboBox / Button etc.). Same shape as WhisperHeim's
  Start-minimised / Launch-at-startup cards.
- **Card with stacked content** (e.g., the Data-path card whose control is
  beneath the description, or the Engine-status card to land via main-032):
  Outer `<StackPanel>` inside the `Border`, label + description as above
  followed by the control(s) below them. Padding/CornerRadius unchanged.
- **Replace every `ui:CardControl` usage on the Settings page** with the
  `Border` pattern above. The visual outcome the user pointed at as "not
  exactly the same" is rooted in `ui:CardControl`'s default chrome
  (background + thinner padding); this swap eliminates it.

### Page titles (resolved during refinement)

| Page | Title strategy |
|---|---|
| Speak | **Hero** (logo + "Mockingbird" 40 pt ExtraBold + "v1.0" tag + tagline) — placed by main-030. main-029 leaves the page in its current shape minus the chrome refit; main-030 lands the hero. |
| Voices | **No hero.** Small `TextBlock` "Voices" `FontWeight="Light"` `FontSize="28"` `Foreground="{DynamicResource TextFillColorPrimaryBrush}"` — matches the existing layout. The cloning card (main-030) and voice list both have their own card-level headings; a hero would compete. |
| Settings | **No hero.** Small `TextBlock` "Settings" `FontWeight="Light"` `FontSize="28"` `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`. Light-theme primary brush renders as dark text on Mica light backdrop — fully readable. The "too dark to read" complaint stemmed from Dark-theme primary (near-black) on a Mica-pulled-towards-white surface; the Light-theme switch above is the actual fix. |
| About | **Hero** — placed by main-032, same composition as Speak's. |

`BrandHeroControl` (a reusable UserControl wrapping the hero composition)
is **not** extracted in this task. Defer to main-030, which is the first
task with two consumers (Speak + About via main-032) and the right place
to factor it out.

### Appearance card on Settings

Add an "Appearance" section to Settings, modelled exactly on WhisperHeim's
General-page picker (lines 187-246):

- Section header: `PaintBrush24` icon + "APPEARANCE" uppercase label per
  the §Section header pattern above.
- Card: `Border` per the §Cards pattern (the **last** card of the section,
  so `Margin="0,0,0,40"`). Inside the Border, a `StackPanel` with:
  - Description line: `TextBlock` "Choose your preferred color scheme."
    `FontSize="13"` `Foreground="{DynamicResource TextFillColorSecondaryBrush}"`
    `Margin="0,0,0,20"`.
  - `UniformGrid Columns="3"` containing three tiles for Light / Dark /
    System. Each tile = `Border Cursor="Hand" Background="Transparent"
    CornerRadius="12" Padding="12"` (transparent so the active-tile
    highlight can be applied via `Background = #19005FAA` on selection).
    Inside each tile: a centred `StackPanel` with an 80x52 swatch
    `Border CornerRadius="8"` (Light = `#FFF9F9F9`, Dark = `#FF2F3131`,
    System = a `LinearGradientBrush` from `#FFF9F9F9` to `#FF2F3131`),
    `BorderBrush="{DynamicResource CardStrokeColorDefaultBrush}"
    BorderThickness="1"`, `Margin="0,0,0,8"`, followed by an uppercase
    label "LIGHT" / "DARK" / "SYSTEM" `FontSize="9" FontWeight="Bold"
    Foreground="{DynamicResource TextFillColorSecondaryBrush}"`.
  - Tile margins: Light `Margin="0,0,8,0"`, Dark `Margin="4,0"`, System
    `Margin="8,0,0,0"` — copied verbatim from WhisperHeim.
- Click handlers (page code-behind, mirroring WhisperHeim
  `GeneralPage.xaml.cs` lines 79-106): each tile's `MouseLeftButtonUp`
  calls a shared `ApplyAppearance(AppearanceMode)` which:
  1. Sets `userSettings.AppearanceMode = mode` (persists to settings.json).
  2. Calls `ApplicationThemeManager.Apply(ApplicationTheme.Light | Dark)`
     for explicit modes, `ApplicationThemeManager.ApplySystemTheme()` for
     System (using `Wpf.Ui.Appearance` per
     [knowledge/research/wpfui-live-theme-swap-2026-05-04.md](../../../knowledge/research/wpfui-live-theme-swap-2026-05-04.md)).
  3. Re-runs a `HighlightActiveTile()` helper that sets the selected
     tile's background to `#19005FAA` and the others to transparent
     (matches WhisperHeim's selected-state cue).

### `UserSettings.AppearanceMode` (new field)

- Add `AppearanceMode` enum (`Light | Dark | System`) in
  `Services\Settings\UserSettings.cs`.
- Add `[JsonPropertyName("appearanceMode")] public AppearanceMode AppearanceMode { get; set; } = AppearanceMode.Light;`
  to the private `SettingsData` class. Wire `JsonStringEnumConverter` on
  the existing `ReadOptions` / `WriteOptions` so the value round-trips as
  the string `"Light"` / `"Dark"` / `"System"` (compatible with
  hand-edited settings.json files).
- Public `AppearanceMode` property follows the existing `OutputDeviceId` /
  `StartMinimised` pattern: setter with equality short-circuit, calls
  `Save()`, fires `AppearanceModeChanged` event.
- Existing installs (no `appearanceMode` field in their `settings.json`)
  default to `Light` **in memory** — the file is **not** rewritten on
  read; only an explicit picker click persists the value. Per
  [ADR 0019](../../../knowledge/decisions/0019-appearance-mode-in-settings-json.md).

### Startup application of the persisted mode

- `EntryPoint` (or whichever startup hook runs before `MainWindow.Show()`)
  resolves `UserSettings` from DI and calls
  `ApplicationThemeManager.Apply(...)` / `ApplySystemTheme()` once with
  the current `AppearanceMode` value.
- This runs on every launch, including the first launch where the
  in-memory default of `Light` is applied — covering the migration path
  for existing Dark-theme installs.

### Styleguide update (`docs/styleguide.md`)

Concrete edits, in order:

1. **§Inherited from WhisperHeim → "Color and contrast principles"** —
   change `dark theme default (ui:ThemesDictionary Theme="Dark" in
   App.xaml)` to **`Light theme default (ui:ThemesDictionary Theme="Light"
   in App.xaml). Per ADR 0019 the active theme is user-selectable via
   Settings → Appearance and persists in settings.json.`**
2. **Add a new §Brand palette section** between "Inherited from
   WhisperHeim" and "Mockingbird divergences" — a four-row table listing
   the brushes (name, hex, when-to-use), the contrast caveat for
   `BrandDeepMutedBrush` ("supplementary numerals such as version tags
   only — fails WCAG body-text contrast on Light backdrop, accepted as
   decorative; matches WhisperHeim verbatim"), and the resource-lookup
   convention (`{StaticResource BrandPrimaryBrush}` from any Page; brushes
   are declared once in App.xaml).
3. **Add a new §Card spec section** under §Reusable component map,
   codifying the `Border CornerRadius=12 Padding=24
   CardBackgroundFillColorDefaultBrush` pattern, the spacing rule (12 px
   between cards, 40 px after a section's last card), and the
   label/description/control composition (DockPanel for one-row cards,
   StackPanel for stacked-content cards). Explicit note: **do not use
   `ui:CardControl`** — it is wpfui's older card chrome and produces a
   visually different surface that drifts away from WhisperHeim.
4. **Add a new §Section header section** alongside §Card spec, codifying
   the `StackPanel Orientation="Horizontal"` + 24-px `ui:SymbolIcon`
   (`Foreground BrandDeepBrush`) + `FontSize=10 FontWeight=Bold` UPPERCASE
   `TextFillColorSecondaryBrush` label composition, with margin
   `0,0,0,20`.
5. **Add a new §Page chrome section** capturing the outer-margin
   (`40,36,40,32`), `MaxWidth=900`, centred `HorizontalAlignment`,
   ScrollViewer `Background="{DynamicResource ApplicationBackgroundBrush}"`
   shell pattern.
6. **Add a new §Appearance modes section** under §Mockingbird divergences,
   noting: three-tile picker on Settings, persisted in `settings.json` as
   `appearanceMode`, default `Light`, live swap via
   `ApplicationThemeManager`. Cross-references ADR 0019 and the
   `wpfui-live-theme-swap-2026-05-04.md` research note.

## Acceptance criteria

- [ ] `App.xaml` `<ui:ThemesDictionary Theme="Light" />` (changed from
      `Dark`).
- [ ] `App.xaml` `<Application.Resources>` `ResourceDictionary` declares
      four `SolidColorBrush x:Key="..."` entries with the exact hex
      values `BrandPrimaryBrush=#FF25abfe`, `BrandAccentBrush=#FFff8b00`,
      `BrandDeepBrush=#FF005FAA`, `BrandDeepMutedBrush=#66005FAA`. The
      brushes resolve from any Page via `{StaticResource ...}` (verified
      by at least one cross-page reference: e.g., the new section-header
      glyphs on Settings).
- [ ] All four pages (Speak / Voices / Settings / About) wrap their
      content in `<ScrollViewer
      Background="{DynamicResource ApplicationBackgroundBrush}">` →
      `<StackPanel Margin="40,36,40,32" MaxWidth="900"
      HorizontalAlignment="Center">`. (Inner page-specific composition is
      unchanged in this task — Speak still has its TextBox+picker+buttons,
      About still has its current panels, etc. main-030 / main-032 will
      restructure those.)
- [ ] Voices page top has a `TextBlock` "Voices" `FontWeight="Light"
      FontSize="28" Foreground="{DynamicResource TextFillColorPrimaryBrush}"`
      (replaces the existing inline header if any).
- [ ] Settings page top has a `TextBlock` "Settings" `FontWeight="Light"
      FontSize="28" Foreground="{DynamicResource TextFillColorPrimaryBrush}"`,
      visibly readable on the Mica Light backdrop (no longer
      near-black-on-near-grey).
- [ ] Settings page has zero `ui:CardControl` usages remaining. Each of
      the existing six setting cards (Default voice, Output device, Start
      minimised, Launch at startup, HTTP port, Stop hotkey, Data path) is
      a `Border CornerRadius=12 Padding=24
      Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"`
      laid out per the §Card spec. Same fields, same controls, same
      bindings — only chrome changes.
- [ ] Settings page has section headers (Audio, App, Diagnostics,
      Appearance) per the §Section header pattern: 24-px
      `ui:SymbolIcon Foreground="{StaticResource BrandDeepBrush}"` +
      uppercase 10-pt bold label. Suggested icons: Audio = `Speaker224`,
      App = `Power24`, Diagnostics = `Wrench24`, Appearance =
      `PaintBrush24`. (Workers may swap if a closer fit exists in the
      Fluent System set.)
- [ ] Card spacing on Settings: `Margin="0,0,0,12"` between cards in the
      same section, `Margin="0,0,0,40"` after the last card of each
      section. Verifiable by visual inspection: the gap before each
      section header is wider than the gap between adjacent cards.
- [ ] Settings page has a new "Appearance" section as the **last**
      section (after Diagnostics), containing one card with a
      `UniformGrid Columns=3` of Light / Dark / System tiles per the
      `What → Appearance card on Settings` spec. Selecting a tile:
      (a) updates `settings.json` `appearanceMode` to the matching
      string, (b) calls `ApplicationThemeManager.Apply` /
      `ApplySystemTheme` (verifiable by toggling: window background and
      card backgrounds change immediately, no restart), (c) highlights
      the selected tile with `Background = #19005FAA`.
- [ ] `UserSettings` exposes a public `AppearanceMode AppearanceMode`
      property (enum `Light | Dark | System`), defaulting to `Light`
      when the field is absent on read. The default does **not** rewrite
      `settings.json` — verifiable by deleting the line, relaunching,
      and confirming the file's mtime is unchanged.
- [ ] Startup path (`EntryPoint` or equivalent) calls
      `ApplicationThemeManager.Apply(...)` /
      `ApplySystemTheme()` once with the current `AppearanceMode`
      **before** `MainWindow.Show()`, so an existing Dark-mode
      preference (or the Light default for fresh installs) renders
      immediately on first paint with no Light→Dark flicker.
- [ ] Speak page background matches the rest of the app — no brighter
      override. (Currently inherits the App.xaml chrome; the
      Light-theme switch should already make this true. Acceptance
      means a worker has eyeballed it and confirmed.)
- [ ] About page background matches the rest of the app, same check
      as Speak.
- [ ] `docs/styleguide.md` reflects all six edits enumerated under
      `What → Styleguide update`. Specifically:
      - "dark theme default" wording is **gone** from §Inherited from
        WhisperHeim.
      - New §Brand palette section exists with the four hex values and
        the contrast caveat.
      - New §Card spec section exists with the `Border` pattern and
        the explicit "do not use `ui:CardControl`" note.
      - New §Section header section exists.
      - New §Page chrome section exists.
      - New §Appearance modes section exists under §Mockingbird
        divergences.
- [ ] `contexts/main/README.md` "Settings page → Out of scope for v1"
      paragraph removes the "theme … toggles" exclusion (this task
      lifts it). The narrative paragraph for the Settings page also
      gains a sentence noting the new Appearance section.

## Notes

- **Resolved during refinement (2026-05-05)**:
  - **Live-swap API**: `Wpf.Ui.Appearance.ApplicationThemeManager.Apply(ApplicationTheme.Light|Dark)`
    + `ApplySystemTheme()`. Verified against WhisperHeim
    `GeneralPage.xaml.cs` lines 79-106. See
    [knowledge/research/wpfui-live-theme-swap-2026-05-04.md](../../../knowledge/research/wpfui-live-theme-swap-2026-05-04.md).
  - **`BrandDeepMutedBrush` contrast**: ~2.2:1 on Light backdrop —
    fails WCAG body-text but the use case (version tag next to a 40 pt
    heading) is decorative supplementary text. Matches WhisperHeim
    verbatim; kept as-is. Documented in styleguide §Brand palette.
  - **Page title strategy**: hero on Speak + About (placed by main-030 /
    main-032), small `FontWeight="Light" FontSize="28"` title on Voices
    + Settings. `BrandHeroControl` extraction deferred to main-030
    (first task with two hero consumers).
  - **Brand brushes location**: `App.xaml` `<Application.Resources>`
    directly. Four brushes is too small for a dedicated
    `Brand.xaml`; merging into wpfui's `ThemesDictionary` would fork
    their chain. Trivial decision; not ADR-worthy.
  - **AppearanceMode default + migration**: default `Light` in memory;
    persist on first explicit toggle. Storage = `settings.json` (per
    [ADR 0019](../../../knowledge/decisions/0019-appearance-mode-in-settings-json.md)),
    not registry — the registry path in ADR 0017 was for a Run-key
    concern with external mutators; appearance is purely in-app.
  - **Split**: NOT split. The user's original note ("single visual
    checkpoint") rules. The fallback split — 029a theme + brushes,
    029b chrome refactor, 029c picker — exists if a worker chokes
    mid-task, but the default flow is one task. See `Worker fallback
    plan` below.
- The Settings-page out-of-scope list in `contexts/main/README.md` calls
  out "theme … toggles" as deferred — that exclusion is being lifted by
  this task. Update the README accordingly when this lands (see the
  matching acceptance criterion).
- This task does **not** touch page composition / layout (Speak's
  button-row position, Voices' clone-card placement, About's content,
  Engine status relocation). Those are main-030 and main-032. main-029
  is **chrome only** plus the new Appearance card.
- Pairs with main-028 (logo) and main-032 (About). Promote together if
  scheduling permits — single visual checkpoint instead of three
  flickers. main-028 is independent (logo asset); main-029 does not
  block on it (this task is palette + chrome, not the mark itself).
- **Worker fallback plan** (only if the single-task scope proves
  unworkable mid-session): split into
  - **029a**: App.xaml theme switch + brand brushes + styleguide §Brand
    palette / §Card spec / §Section header / §Page chrome additions.
    Foundation, no UI behaviour change.
  - **029b**: Page chrome refactor across all four pages — replaces
    `ui:CardControl` on Settings, applies the outer margin / MaxWidth /
    ScrollViewer pattern everywhere, swaps the Settings/Voices titles
    to `FontWeight="Light" FontSize="28"`. Consumes the brushes/cards
    from 029a.
  - **029c**: Appearance picker — new section on Settings, new
    `UserSettings.AppearanceMode` field + ADR 0019, startup
    application path, styleguide §Appearance modes section. Depends
    on 029a.
  Splitting is **discretionary**, not required. The single-task path is
  preferred and the acceptance criteria above describe it as one
  delivery.

## Outcome

Single-task path completed end-to-end on 2026-05-05; no split needed.

**Theme + brand palette.** `App.xaml` flipped to `Theme="Light"`, and the
four brand brushes (`BrandPrimaryBrush #FF25abfe`, `BrandAccentBrush
#FFff8b00`, `BrandDeepBrush #FF005FAA`, `BrandDeepMutedBrush #66005FAA`)
declared as `SolidColorBrush x:Key="…"` directly inside
`<Application.Resources>` so any Page resolves them via `{StaticResource
…}`. Settings page section-header glyphs reference `BrandDeepBrush` —
verifies cross-page lookup works.

**Page chrome.** All four pages (Speak, Voices, Settings, About) now wrap
content in the standard
`<ScrollViewer Background="{DynamicResource ApplicationBackgroundBrush}">`
→ `<StackPanel Margin="40,36,40,32" MaxWidth="900"
HorizontalAlignment="Center">` shell. Inner page-specific composition is
unchanged per the chrome-only scope (Speak's TextBox+picker+button row,
Voices' list + cloning panel, About's hero + Engine status panel + View
logs all retained verbatim).

**Settings page card refit.** Every `ui:CardControl` on Settings replaced
with the `Border CornerRadius=12 Padding=24
Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"`
pattern. One-row cards use `DockPanel` (left label/description, right
control); the Data path card and the Appearance card use the
stacked-content `StackPanel` variant. Section headers added (Audio /
Speaker224, App / Power24, Diagnostics / Wrench24, Appearance /
PaintBrush24) per the §Section header pattern. Card spacing rule applied:
12 px between cards in a section, 40 px on the last card before the next
section.

**Appearance picker.** New section at the bottom of Settings hosts a
`UniformGrid Columns="3"` of Light / Dark / System tiles, copied verbatim
from WhisperHeim's `GeneralPage.xaml` lines 187-246 (swatch sizes 80×52,
Light `#FFF9F9F9`, Dark `#FF2F3131`, System linear gradient). Click
handlers on the page code-behind call a shared `ApplyAppearance(mode)`
which (1) writes `userSettings.AppearanceMode = mode`, (2) calls
`EntryPoint.ApplyAppearanceMode(mode)` (which dispatches to
`ApplicationThemeManager.Apply(Light|Dark)` /
`ApplicationThemeManager.ApplySystemTheme()`), (3) re-runs
`HighlightActiveAppearanceTile()` so the selected tile picks up the
`#19005FAA` highlight. `OnNavigatedTo` re-highlights from the persisted
mode so reopening the page reflects the live state.

**`UserSettings.AppearanceMode`.** New `AppearanceMode` enum (`Light |
Dark | System`) and a public property on `UserSettings` mirroring the
`StartMinimised` setter pattern (equality short-circuit → `Save()` →
`AppearanceModeChanged` event). Backed by
`SettingsData.AppearanceMode` annotated
`[JsonPropertyName("appearanceMode")]` with default
`AppearanceMode.Light`. `JsonStringEnumConverter` registered on both
`ReadOptions` and `WriteOptions` so the value round-trips as the JSON
string `"Light" | "Dark" | "System"`. The default is in-memory only —
existing settings.json files without an `appearanceMode` field are not
rewritten on read (Save runs only on explicit assignment with a different
value, per the equality short-circuit). Per ADR 0019, now flipped from
Proposed to Accepted.

**Startup application.** `EntryPoint.Run` calls
`ApplyAppearanceMode(userSettings.AppearanceMode)` once after the host is
built and before `MainWindow.Show()` (or `window.Show() + window.Hide()`
in the `StartMinimised` path). Fresh installs paint Light immediately;
existing Dark-mode preferences (or future Light installs that toggled
Dark) paint correctly on first frame with no flicker.

**Page titles.** Voices and Settings now lead with a `TextBlock
FontWeight="Light" FontSize="28"
Foreground="{DynamicResource TextFillColorPrimaryBrush}"` heading per the
small-heading strategy. Speak and About retain their current shape (heros
land via main-030 / main-032).

**Styleguide.** Six edits applied to `docs/styleguide.md`:
1. §Inherited from WhisperHeim → "Color and contrast principles" rewritten
   from "dark theme default" to "Light theme default … per ADR 0019 the
   active theme is user-selectable via Settings → Appearance and persists
   in settings.json".
2. New top-level §Brand palette section with the four-row brush table,
   contrast caveat for `BrandDeepMutedBrush`, and the
   `{StaticResource …}` lookup convention.
3. New top-level §Card spec section with the `Border` pattern, spacing
   rule (12 / 40 px), one-row vs stacked-content compositions, and the
   explicit "do not use `ui:CardControl`" prohibition.
4. New top-level §Section header section with the
   `StackPanel Orientation="Horizontal"` + 24-px `ui:SymbolIcon` +
   uppercase 10-pt bold label composition.
5. New top-level §Page chrome section with the ScrollViewer + StackPanel
   shell pattern (40,36,40,32 margin, MaxWidth=900, centred,
   `ApplicationBackgroundBrush` background) plus the hero / small-heading
   title strategies.
6. New §Appearance modes section under §Mockingbird divergences cross-
   referencing ADR 0019 and the wpfui-live-theme-swap research note.

**README.** `contexts/main/README.md` Settings page narrative updated:
the "theme … toggles" out-of-scope exclusion is lifted (and a marker
sentence added so the change is auditable), the §Appearance section is
documented inline, and the surrounding paragraph notes the wholesale
`ui:CardControl` → `Border` swap.

**Build verification.** `dotnet build mockingbird.sln -c Debug` → 0 errors,
0 warnings. Interactive verification (page renders, tile click swaps
theme live without restart, settings.json round-trips correctly,
existing-install migration path) is **assume-pass** per the standing
project convention — code paths are in place per the spec; the user will
eyeball after the next launch.

**Key files changed**:
- `src/Mockingbird/App.xaml`
- `src/Mockingbird/EntryPoint.cs`
- `src/Mockingbird/Services/Settings/UserSettings.cs`
- `src/Mockingbird/Views/Pages/SpeakPage.xaml`
- `src/Mockingbird/Views/Pages/VoicesPage.xaml`
- `src/Mockingbird/Views/Pages/SettingsPage.xaml`
- `src/Mockingbird/Views/Pages/SettingsPage.xaml.cs`
- `src/Mockingbird/Views/Pages/AboutPage.xaml`
- `docs/styleguide.md`
- `.agenthoff/contexts/main/README.md`
- `.agenthoff/knowledge/decisions/0019-appearance-mode-in-settings-json.md`
  (Proposed → Accepted)
