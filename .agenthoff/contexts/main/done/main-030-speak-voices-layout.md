---
id: main-030
title: Speak page hero + button row above text; Voices clone card above list
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-05
commit:
depends_on: [main-010, main-028, main-029]
blocks: [main-032]
tags: [speak, voices, layout, ui]
---

## Why

Two layout problems on the daily-use surfaces:

1. **Speak page has no identity** — opens straight into a textbox. WhisperHeim's
   pages all open with the hero (logo + name + version + tagline). Without it,
   Mockingbird feels like a textarea with controls, not an app.
2. **Speak controls are below the textbox.** Voice picker is row 1, then a
   row of Play / Stop / Save buttons below it. The user wants the picker and
   buttons in **one row above the text input**: voice on the left, then Play /
   Stop / Save in the same horizontal strip. The textbox dominates the
   remaining space.
3. **Voices page has the clone panel below the list.** Per main-025 the cloning
   sub-panel is the third row beneath the voice list. The user wants the
   "Clone a new voice" card **above** the preview list — clone first, browse
   second.

## What

### Reusable pieces (this task ships them)

`BrandHeroControl` — a new `UserControl` in
`src/Mockingbird/Views/Controls/BrandHeroControl.xaml(.cs)`. Composition matches
WhisperHeim's hero exactly: 88×88 logo badge (brand-blue border + tinted
background) on top, "Mockingbird" 40 pt ExtraBold beneath, version tag in
`BrandDeepMutedBrush` next to or below the name, optional tagline in 14 pt
secondary at the bottom. Logo and app name are app-wide constants; version
comes from the new `AppInfo` helper (see below); tagline is the only
per-instance variable.

- `Tagline` — `DependencyProperty` of type `string`. When `null`/empty, the
  tagline `TextBlock` is collapsed (`Visibility=Collapsed`). Speak passes
  `null` (so the hero shows logo + name + version only); About passes
  `"Local voices for Claude Code"` in main-032.
- The control reads the version through `AppInfo.Version` (no per-instance
  version property — there's only one running app).

`AppInfo` — a new static helper in `src/Mockingbird/Services/AppInfo.cs` that
encapsulates the assembly-version lookup currently inlined in
`AboutPageViewModel`. Returns the same string the About page already shows:
`AssemblyInformationalVersionAttribute` with any `+sha` suffix stripped,
falling back to `AssemblyName.Version` (3-part) then `"unknown"`. main-032
will move `AboutPageViewModel`'s inline lookup over to this helper as part of
its own scope.

### Speak page

Restructure `SpeakPage.xaml` (the page composition stays a `ui:Page`, the VM
unchanged unless noted):

1. **Top: hero block** — `<controls:BrandHeroControl />` with no `Tagline`
   set (so just logo + name + version, no tagline string under the version).
   Centered horizontally with the styleguide's outer margin.
2. **Controls row** — single horizontal row containing: voice picker
   (`ComboBox`, MinWidth 240, label-less if visual rhythm carries; otherwise
   "Voice" label) on the left, then **Play (Primary), Stop (Secondary),
   Save (Secondary, with the existing inline `ui:ProgressRing`)** in that
   exact order, no extra separator gap before Save. WhisperHeim TTS lays
   them all together; we inherit verbatim.
3. **Text input** — multi-line `ui:TextBox` filling the rest of the page.
   `AcceptsReturn=True`, `MinHeight` raised so that with the hero+row above it,
   typical text doesn't push the row off-screen.
4. **Status line** at the bottom unchanged.

Behavioural reminder: Save's `IsRunning` continues to surface the inline
progress ring; PlayCommand's CanExecute (`NotifyCanExecuteChangedFor` on Text +
SelectedVoice) is unchanged. The VM's `StatusChanged` subscription, the
`VoiceCatalog.VoicesChanged` subscription, the `INavigationAware` lifecycle
(`OnNavigatedTo` / `OnNavigatedFrom`) — all unchanged.

### Voices page

Restructure `VoicesPage.xaml`:

1. **Small page title** — `TextBlock Text="Voices" FontWeight="Light"
   FontSize="28"` on its own row, left-aligned with the styleguide outer
   margin. **Not the big hero** — the big hero is reserved for Speak and
   About per the user's framing. Treatment matches Settings (per main-029).
2. **Clone a new voice card** — moves to **above** the voice list, styled as
   a single WhisperHeim card containing the cloning sub-panel (source toggle
   + recording controls + voice-name input + Save). Visually a section heading
   on the card itself ("Clone a new voice" `SemiBold`).
3. **Voice library list** — Built-in section + Cloned section, identical row
   templates to today (preview button + per-row delete on cloned rows). Sits
   below the cloning card.

The page-VM relationships (`VoicesPageViewModel.Cloning` composition,
`LibraryChanged` → `VoicesChanged` refresh) all stay; this is a XAML-only
change for the layout swap.

## Acceptance criteria

- [ ] `BrandHeroControl` `UserControl` ships under
      `src/Mockingbird/Views/Controls/`. Renders 88×88 logo badge + name
      "Mockingbird" 40 pt ExtraBold + version tag in `BrandDeepMutedBrush`,
      with optional tagline `TextBlock` (visibility-collapsed when
      `Tagline` `DependencyProperty` is null/empty).
- [ ] `AppInfo.Version` static property in `src/Mockingbird/Services/`
      returns the same version string `AboutPageViewModel`'s inline lookup
      currently produces (`AssemblyInformationalVersionAttribute` + `+sha`
      stripping + fallback chain).
- [ ] Speak page opens with `<controls:BrandHeroControl />` (no tagline) at
      the top, centered, with the styleguide's outer margin.
- [ ] Speak page has a single horizontal controls row containing voice
      picker + Play + Stop + Save — in that order, no separator gap —
      **above** the text input.
- [ ] Speak page text input fills the remaining vertical space below the
      controls row; `AcceptsReturn=True` preserved.
- [ ] Speak page Save command continues to surface the inline
      `ui:ProgressRing` via `IsRunning`.
- [ ] Voices page opens with a small `FontWeight="Light" FontSize="28"
      Text="Voices"` title (no big hero).
- [ ] Voices page has the "Clone a new voice" card **above** the voice
      library list (no longer the third row at the bottom).
- [ ] Cloning behaviour is unchanged: source toggle, level meter, duration,
      voice-name validation, Save flow, error UX, post-save row appearance
      via `LibraryChanged` — all still work without re-navigation.
- [ ] Voice library list still renders Built-in section + Cloned section with
      preview + per-row delete affordances intact.
- [ ] Page chrome matches main-029 styleguide spec (40,36,40,32 outer margin,
      MaxWidth=900, centered, brand palette).
- [ ] Build is clean (`dotnet build mockingbird.sln -c Debug` →
      0 errors, 0 warnings).

## Notes

- Consumed by main-032: `BrandHeroControl` + `AppInfo` are both reused on
  the redesigned About page. main-032 declares `depends_on: [main-030, …]`.
- `AppInfo` is a static helper, not a DI service. It has no state and no
  test seam; the version lookup is a one-liner against
  `Assembly.GetExecutingAssembly()`. If a future test needs to mock it,
  promote then; not now.
- `BrandHeroControl` is **not** a wpfui control — it's a plain WPF
  `UserControl` styled to match the WhisperHeim hero. No fancy
  inheritance; just XAML + a single `DependencyProperty`.
- Pairs with main-028 (mark) and main-029 (theme + cards). Don't ship this
  before the other two unless the user explicitly wants partial visual
  progress — the hero looks wrong without the new mark and palette.

## Resolution log (refinement 2026-05-05)

The three open questions in the prior backlog version were resolved as
follows:

| Question | Resolution |
|---|---|
| Voices page: big hero or small title? | Small title only (`Light 28pt "Voices"`). Big hero is Speak + About only. |
| Button row composition? | All three together in WhisperHeim order — Play (Primary), Stop (Secondary), Save (Secondary). No separator gap. |
| Extract `BrandHeroControl`? | Yes, ship in main-030. Reused by main-032's About page. `Tagline` is the only `DependencyProperty`. |

Side effect: `AppInfo` static helper extracted as part of this task so
`BrandHeroControl` can read the version without per-page plumbing. main-032
migrates `AboutPageViewModel`'s inline version lookup over to it.
`blocks: [main-032]` added to mirror the new dependency edge.

## Outcome

Reusable `BrandHeroControl` shipped at
`src/Mockingbird/Views/Controls/BrandHeroControl.xaml(.cs)` — 88x88 logo
badge (`BrandPrimaryBrush` border + `#1025abfe` tinted background) + name
in 40pt ExtraBold + `vX.Y.Z` tag in `BrandDeepMutedBrush` + optional
`Tagline` `DependencyProperty` whose backing read-only `HasTagline` DP
collapses the tagline `TextBlock` when null/whitespace. Version is read
from `AppInfo.Version` on construction.

`AppInfo` static helper shipped at `src/Mockingbird/Services/AppInfo.cs` —
encapsulates the `AssemblyInformationalVersionAttribute` lookup with `+sha`
stripping and 3-part / `"unknown"` fallback. Same algorithm
`AboutPageViewModel.ResolveVersion` already used; main-032 will migrate
the inline lookup over to the helper as part of its scope.

Speak page (`src/Mockingbird/Views/Pages/SpeakPage.xaml`) restructured
into a 4-row `Grid` (replacing the prior `ScrollViewer`+`StackPanel` so the
`*` row can actually expand): hero with no tagline, single horizontal
controls row (voice picker MinWidth=240 + Play Primary + Stop Secondary +
Save Secondary in WhisperHeim TTS order, no separator gap before Save),
multi-line `ui:TextBox` filling the `*` row (`AcceptsReturn=True`,
`MinHeight=240`), status `TextBlock` at the bottom. Save's inline
`ui:ProgressRing` tied to `SaveCommand.IsRunning` preserved verbatim.

Voices page (`src/Mockingbird/Views/Pages/VoicesPage.xaml`) reordered:
small Light/28 "Voices" title (unchanged from main-029), then the
**Clone a new voice** card (now styled as a single WhisperHeim
`Border CornerRadius=12 Padding=24` per styleguide §Card spec) **above**
the voice library list, then the Built-in / Cloned sections. Cloning
behaviour, source toggle, level meter, duration, voice-name validation,
Save flow, error UX, and post-save `LibraryChanged` → `VoicesChanged`
refresh chain all unchanged (XAML-only swap). Empty-cloned-section
copy updated to "No cloned voices yet — clone one above."

Build is clean: `dotnet build mockingbird.sln -c Debug` → 0 errors,
0 warnings.

### Key files

- `src/Mockingbird/Views/Controls/BrandHeroControl.xaml(.cs)` — new control
- `src/Mockingbird/Services/AppInfo.cs` — new static helper
- `src/Mockingbird/Views/Pages/SpeakPage.xaml` — 4-row Grid layout
- `src/Mockingbird/Views/Pages/VoicesPage.xaml` — clone card moved above list

### BC README

Updated to register `Views/Controls/BrandHeroControl.xaml(.cs)` and
`Services/AppInfo.cs` in the code-structure tree, rewrote the §Speak page
opener to describe the new 4-row hero+controls+text+status layout, and
relabelled §Cloning panel to §Cloning panel (main-025, relocated by
main-030) to capture the above-the-list reorder + WhisperHeim-card
re-skin. No new ADR — the layout decisions were resolved in the task
spec's Resolution log; no architectural choice was made during execution.

### Verification note

Build is clean. Interactive UI behaviours — hero rendering centred above
the controls row, textbox dominating the remaining vertical space without
pushing the controls off-screen, voice-picker pre-selection, Save's
ProgressRing while async runs, clone card sitting above the voice list,
all cloning behaviours unchanged — are **not interactively re-tested** in
this pass per the project's convention; the code is in place per the
main-030 spec and any regression will surface during the next manual run.
