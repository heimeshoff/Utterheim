---
id: main-032
title: Relocate engine diagnostics; redesign About to match WhisperHeim
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-05
commit: 90dfe39
depends_on: [main-010, main-028, main-029, main-030]
blocks: []
tags: [about, settings, navigation, ui, branding]
---

## Why

Two related restructurings:

1. **The About page is overloaded.** main-017 put the engine status panel,
   the Restart Engine button, and the View logs link on About. Those are
   diagnostic controls, not "about the app" content. The user wants them on
   Settings (at the bottom of the page), keeping About for app identity,
   credits, and contact / support links — the WhisperHeim About-page
   composition.
2. **About should sit at the bottom-left of the navigation pane**, like
   WhisperHeim. wpfui's `ui:NavigationView` exposes `FooterMenuItems` for
   that placement; today About is in the regular `MenuItems` list with the
   other three pages.
3. **About content needs to mirror WhisperHeim's About**: hero (logo + name +
   version + tagline), profile-and-contact card with the Marco Heimeshoff
   bio + "Get in Touch" links, and a support card with Ko-fi button and
   "View on GitHub" link — pointing at the **mockingbird** repo, not the
   WhisperHeim one.

## What

### Move engine diagnostics to Settings

- **Remove from About**: engine-status panel (state pip + label + port +
  healthy + last-error block + Restart Engine button) and the View logs
  hyperlink.
- **Extract `EngineStatusCardViewModel`** in
  `src/Mockingbird/ViewModels/EngineStatusCardViewModel.cs` (note:
  parallel to the ViewModels/Pages/ folder, not inside it — this VM is
  composed, not a page VM). Carries the rich panel's state:
  `EngineState`, `Healthy`, `Port`, `LastError`, `EngineStateLabel`,
  `PortLabel`, `IsRetryEnabled`, plus the `RestartEngineCommand` and
  `OpenLogsCommand`. Subscribes to `SidecarHost.StateChanged`; re-seeds
  via `SidecarHost.GetStatus()` when its `Attach()` is called and detaches
  on `Detach()`. Mirrors the `VoicesPageViewModel.Cloning` composition
  pattern (a sub-VM owned by the parent page VM).
- **Compose into `SettingsPageViewModel`** as
  `public EngineStatusCardViewModel EngineStatus { get; }`. Constructor
  injects it from DI (registered transient — fresh per Settings-page
  resolution). `SettingsPage.OnNavigatedTo` calls
  `ViewModel.EngineStatus.Attach()`, `OnNavigatedFrom` calls `Detach()`.
- **Add to Settings → Diagnostics, at the end of the section** (after
  the existing HTTP port + Stop hotkey + Data path cards):
  - **Engine status card** containing the same panel composition main-017
    landed on About (state pip, port, healthy indicator, last-error block,
    Restart Engine button), bound to `EngineStatus.*`.
  - **View logs** `ui:HyperlinkButton` underneath the engine card, bound
    to `EngineStatus.OpenLogsCommand`.
- **Strip the engine-status fields from `AboutPageViewModel`** entirely.
  After this task, `AboutPageViewModel` carries only `Version` (read from
  the new `AppInfo.Version` helper that main-030 ships). `Attach()` /
  `Detach()` go away.
- The persistent footer (HTTP + Engine state, backed by the existing
  `EngineStatusViewModel`) **stays** — it remains the always-visible
  engine signal, complemented by the fuller Settings panel. Per main-017's
  Q2 note this dual surface is intended.

### Move About to footer of nav

- Wrap `MainWindow.xaml` `ui:NavigationView`: keep Speak / Voices / Settings
  in `MenuItems`, move the About `NavigationViewItem` to
  `ui:NavigationView.FooterMenuItems`. About then sits at the bottom-left of
  the nav pane like WhisperHeim's About.

### Redesign the About page

Replace the current page composition with the WhisperHeim About structure
(see [WhisperHeim AboutPage.xaml](../../../../tooling/WhisperHeim/src/WhisperHeim/Views/Pages/AboutPage.xaml)
lines 9–230 — adapt section by section):

1. **Hero** — `<controls:BrandHeroControl Tagline="Local voices for Claude
   Code" />` from main-030. Shows the main-028 mark inside the brand-blue
   badge, "Mockingbird" 40 pt ExtraBold, the version tag in
   `BrandDeepMutedBrush`, and the tagline beneath.
2. **Profile & Contact card** — `Border` with
   `CardBackgroundFillColorSecondaryBrush`, `CornerRadius=12`, `Padding=28`.
   Inside:
   - Round 140×140 portrait of Marco. **Asset**: copy
     `tooling/WhisperHeim/src/WhisperHeim/Assets/heimeshoff.jpg` into
     `assets/people/heimeshoff.jpg` and add as `<Resource>` to
     `Mockingbird.csproj`. Render with
     `BorderBrush=#FF25abfe BorderThickness=3` ring (identical to
     WhisperHeim).
   - Bio paragraph 1 (verbatim from WhisperHeim — Marco's identity is the
     same on both apps): *"Hi, I'm **Marco Heimeshoff** — trainer,
     consultant, and conference organiser focused on **Domain-Driven
     Design** and **collaborative modeling**."*
   - Bio paragraph 2 (mockingbird-specific, replaces WhisperHeim's
     ubiquitous-language line): *"I built Mockingbird so my Claude Code
     sessions could speak in different voices — local, offline, with
     whatever voices I want to clone."*
   - Divider.
   - "Get in Touch" sub-section with the same three rows (heimeshoff.de
     Globe, @Heimeshoff.de Bluesky with the official Bluesky butterfly
     path, linkedin.com/in/heimeshoff with the LinkedIn glyph). Identical
     to WhisperHeim — same hyperlinks, same colours.
3. **Support & GitHub card** — `Border` `CardBackgroundFillColorSecondaryBrush`
   `CornerRadius=12` `Padding=28`. Inside:
   - "If you enjoy Mockingbird and want to support my open-source work,
     you can buy me a coffee!" line (text adapted from WhisperHeim).
   - Ko-fi button — gradient `#FF25abfe → #FF005FAA`, `CornerRadius=10`,
     "Buy me a coffee on Ko-fi" white text. Click navigates to
     `https://ko-fi.com/heimeshoff` (the same Ko-fi page WhisperHeim uses,
     verified via grep). The URL is stored as a constant on `AppInfo`
     (`AppInfo.KofiUrl`) so it's a single-source rename target if it ever
     moves.
   - Closing line "Otherwise, just **enjoy using Mockingbird for free** —
     and thanks for giving it a try!"
   - GitHub row: Star icon + "View on GitHub" hyperlink. URL stored as
     `AppInfo.GithubUrl = "https://github.com/heimeshoff/mockingbird"`.
4. **Credits** at the bottom (smaller, unchanged): "Synthesis powered by
   pocket-tts (Kyutai Labs)."

The Bento grid (Philosophy + AI Models) at the very bottom of WhisperHeim's
About is **not** copied — it's WhisperHeim-specific and overlaps with
mockingbird's Settings → Engine status. Drop it.

### Converters consolidation

`EngineStateToPipBrushConverter` and `NullOrEmptyToVisibilityConverter`
currently live in
`src/Mockingbird/ViewModels/Pages/AboutPageConverters.cs`.
`NullOrEmptyToVisibilityConverter` is **also** duplicated in
`VoicesPageConverters.cs` (verified via grep on the codebase).

- Move both converters into a new file
  `src/Mockingbird/Views/Converters/SharedConverters.cs` (or the existing
  converter folder if one is established).
- Delete the duplicate from `VoicesPageConverters.cs`. If
  `VoicesPageConverters.cs` ends up empty after the move, delete it.
- Register both in `App.xaml`'s `<Application.Resources>` so any page
  XAML can reference them by `StaticResource` without per-page namespace
  imports.
- Update `AboutPage.xaml`, `VoicesPage.xaml`, `DeleteVoiceDialog.xaml`,
  and the new `SettingsPage.xaml` engine-card markup to consume the
  shared resources.

## Acceptance criteria

- [ ] About `NavigationViewItem` is moved to
      `ui:NavigationView.FooterMenuItems` in `MainWindow.xaml`; About
      appears at the bottom-left of the nav pane.
- [ ] Engine status panel (state pip / port / healthy / last error /
      Restart Engine) is **gone from About** and now lives at the end
      of Settings → Diagnostics, bound through `EngineStatus.*` on
      `SettingsPageViewModel`.
- [ ] View logs is **gone from About** and now lives at the end of
      Settings → Diagnostics, beneath the engine card.
- [ ] `EngineStatusCardViewModel` exists as its own VM. Subscribes to
      `SidecarHost.StateChanged` on `Attach()`; unsubscribes on
      `Detach()`. `Attach()` is called from `SettingsPage.OnNavigatedTo`,
      `Detach()` from `OnNavigatedFrom`.
- [ ] `AboutPageViewModel` no longer carries `EngineState`, `Healthy`,
      `Port`, `LastError`, `LastErrorVisibility`, `EngineStateLabel`,
      `PortLabel`, `IsRetryEnabled`, `RestartEngineCommand`, or
      `OpenLogsCommand`. It carries `Version` (sourced from
      `AppInfo.Version` per main-030).
- [ ] `RestartEngineCommand` and `OpenLogsCommand` continue to work
      identically (call `SidecarHost.RestartAsync` and open
      `%LOCALAPPDATA%\Mockingbird\logs\` in Explorer). The state pip is
      live via `SidecarHost.StateChanged`.
- [ ] Persistent footer (HTTP + Engine state) is unchanged — still backed
      by `EngineStatusViewModel`.
- [ ] About page composition matches the WhisperHeim About structure:
      hero (`<controls:BrandHeroControl Tagline="Local voices for Claude
      Code" />`), profile-and-contact card, support / GitHub card,
      credits. No engine status, no diagnostics, no AI-models bento.
- [ ] About profile card carries the 140×140 ringed `heimeshoff.jpg`
      portrait (copied into `assets/people/`), Marco's two-paragraph bio
      (paragraph 1 verbatim from WhisperHeim, paragraph 2 the
      mockingbird-specific voice-diversity line), and the
      heimeshoff.de / Bluesky / LinkedIn "Get in Touch" rows with
      identical hyperlinks and icons to WhisperHeim.
- [ ] Support card has the Ko-fi gradient button (URL via
      `AppInfo.KofiUrl = "https://ko-fi.com/heimeshoff"`) and a
      "View on GitHub" link (URL via
      `AppInfo.GithubUrl = "https://github.com/heimeshoff/mockingbird"`).
- [ ] `AppInfo` (shipped by main-030) gains `KofiUrl` and `GithubUrl`
      static constants in this task, alongside the existing `Version`.
- [ ] `EngineStateToPipBrushConverter` and
      `NullOrEmptyToVisibilityConverter` live in a single shared
      converters file under `Views/Converters/` and are registered as
      `App.xaml` resources. The duplicate definition in
      `VoicesPageConverters.cs` is removed; pages reference the shared
      resources via `StaticResource`.
- [ ] Build is clean (`dotnet build mockingbird.sln -c Debug` →
      0 errors, 0 warnings).

## Notes

- `BrandHeroControl` is shipped by main-030. This task only **consumes**
  it (passes `Tagline="Local voices for Claude Code"`). If main-030 has
  not landed yet, this task is blocked.
- The `AppInfo` static helper is also shipped by main-030 (with `Version`).
  This task **extends** it with `KofiUrl` and `GithubUrl` constants.
- `EngineStatusCardViewModel` is registered transient in DI so each Settings
  page resolution gets a fresh instance — same lifetime
  `VoiceCloningViewModel` uses for the same reason.
- The WhisperHeim About uses `UserControl` as the page root while
  Mockingbird Pages use `Page` (from main-020 nav shell). Stay on `Page`;
  the XAML pattern is identical aside from the root element name.
- Bluesky and LinkedIn icon paths/glyphs: lift verbatim from
  `WhisperHeim/Views/Pages/AboutPage.xaml` (lines around the contact
  rows). They're inline `<Path>` data; no new asset files needed beyond
  the portrait.
- Pairs with main-028 (mark), main-029 (theme + cards), and main-030
  (`BrandHeroControl` + `AppInfo`). Don't ship before all three.

## Resolution log (refinement 2026-05-05)

The four open questions in the prior backlog version were resolved as
follows:

| Question | Resolution |
|---|---|
| Marco's portrait? | Reuse WhisperHeim's `heimeshoff.jpg`. Copy the file into `assets/people/heimeshoff.jpg`. |
| Ko-fi URL? | Shared with WhisperHeim: `https://ko-fi.com/heimeshoff` (verified via grep). Stored as `AppInfo.KofiUrl` constant. |
| About copy — voice-diversity wording? | Keep WhisperHeim's bio paragraph 1 verbatim. Replace paragraph 2 with: *"I built Mockingbird so my Claude Code sessions could speak in different voices — local, offline, with whatever voices I want to clone."* |
| Engine status VM placement? | Extract `EngineStatusCardViewModel`. Compose into `SettingsPageViewModel.EngineStatus`. Mirrors the `VoicesPageViewModel.Cloning` pattern. |

Side effects:
- `depends_on` extended from `[main-010, main-028, main-029]` to
  `[main-010, main-028, main-029, main-030]` because main-030 now ships
  `BrandHeroControl` and the `AppInfo` helper this task consumes.
- `AppInfo` gains `KofiUrl` + `GithubUrl` constants in this task.
- `NullOrEmptyToVisibilityConverter` duplication (currently in
  `AboutPageConverters.cs` AND `VoicesPageConverters.cs`) is consolidated
  into a shared converters file as part of the engine-status move.

## Outcome

Done 2026-05-05. Build clean (0 errors / 0 warnings). All acceptance
criteria met.

**About → identity-only surface mirroring WhisperHeim.** Engine diagnostics
(state pip / port / healthy / last error / Restart Engine + View logs)
moved from About to the bottom of Settings → Diagnostics. About is now
hero (`BrandHeroControl Tagline="Local voices for Claude Code"`) +
profile-and-contact card (140x140 ringed `heimeshoff.jpg` + bio +
heimeshoff.de / Bluesky / LinkedIn rows) + Support & GitHub card (Ko-fi
gradient button → `AppInfo.KofiUrl`; "View on GitHub" → `AppInfo.GithubUrl`)
+ credits. About moved to `ui:NavigationView.FooterMenuItems` so it sits
at the bottom-left of the nav pane.

Key files:
- `src/Mockingbird/ViewModels/EngineStatusCardViewModel.cs` (new) — extracted
  sub-VM, composed onto `SettingsPageViewModel.EngineStatus`, registered
  transient (mirrors the `VoiceCloningViewModel` pattern).
- `src/Mockingbird/Views/Pages/AboutPage.xaml` + `.xaml.cs` (rewritten) —
  WhisperHeim-style composition; code-behind handles `Hyperlink_RequestNavigate`
  + `KofiButton_Click`.
- `src/Mockingbird/ViewModels/Pages/AboutPageViewModel.cs` (collapsed) — only
  `Version` (sourced from `AppInfo.Version`) remains; no commands, no
  subscriptions, no `Attach()` / `Detach()`.
- `src/Mockingbird/Views/Pages/SettingsPage.xaml` + `SettingsPageViewModel.cs`
  + `SettingsPage.xaml.cs` — engine card + View logs added at end of
  Diagnostics; `Attach()`/`Detach()` forwarded to the composed sub-VM.
- `src/Mockingbird/Services/AppInfo.cs` — extended with `KofiUrl` +
  `GithubUrl` constants.
- `src/Mockingbird/Views/Converters/SharedConverters.cs` (new) — consolidates
  `NullOrEmptyToVisibilityConverter` (previously duplicated in
  `AboutPageConverters.cs` + `VoicesPageConverters.cs`) and
  `EngineStateToPipBrushConverter`. Both registered as `App.xaml` resources
  alongside the WPF built-in `BoolToVisibilityConverter` so any Page can
  reference them via `{StaticResource ...}` without per-page imports.
- `src/Mockingbird/ViewModels/Pages/VoicesPageConverters.cs` — duplicate
  removed; only `CloningSourceToBoolConverter` remains.
- `src/Mockingbird/ViewModels/Pages/AboutPageConverters.cs` — deleted.
- `src/Mockingbird/Views/MainWindow.xaml` — About moved to `FooterMenuItems`.
- `src/Mockingbird/Views/Pages/VoicesPage.xaml` +
  `src/Mockingbird/Views/Dialogs/DeleteVoiceDialog.xaml` — local converter
  declarations dropped; both pages now resolve from App.xaml.
- `src/Mockingbird/EntryPoint.cs` — `EngineStatusCardViewModel` registered
  transient.
- `src/Mockingbird/App.xaml` — three converters registered at app scope.
- `src/Mockingbird/Mockingbird.csproj` — `assets/people/heimeshoff.jpg`
  added as `<Resource>`.
- `assets/people/heimeshoff.jpg` (new) — copied verbatim from
  `tooling/WhisperHeim/src/WhisperHeim/Assets/heimeshoff.jpg`.

ADR written: `0021-engine-diagnostics-on-settings-not-about.md` — records
the rationale for the split (identity vs diagnostics) and the
`EngineStatusCardViewModel` composed-sub-VM pattern.

The interactive UI behaviours (live pip flicker on engine state changes,
Restart Engine cycling, View logs opening Explorer, About in the nav
footer, Ko-fi / GitHub navigation, Bluesky / LinkedIn icons rendering) are
not interactively re-tested in this pass; the code is in place per the
spec and any regression will surface during the next manual run.
