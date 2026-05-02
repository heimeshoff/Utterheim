---
id: main-020
title: Navigation shell — wpfui NavigationView with four-page skeleton
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-02
commit: a19e9b3
depends_on: [main-010]
blocks: [main-013, main-014, main-016, main-017]
tags: [frontend, foundation, navigation]
---

## Why

The walking skeleton's `MainWindow.xaml` is a placeholder splash — a static
two-column `Grid` with a sidebar `StackPanel` holding one disabled "Speak"
button. There is no `NavigationView`, no page abstraction, no routing.

The styleguide canonicalised four pages (Speak / Voices / Settings / About)
delivered by main-013, main-014, main-016, main-017. Every one of those tasks
needs the same primitive: a left-sidebar `ui:NavigationView`, a page
abstraction, and a "page is now visible" lifecycle event. Putting that
primitive in any one of the page tasks would force the other three to wait
behind it on a feature commitment they don't share.

This task delivers the shell only. Each page is a stub ("Coming soon — see
main-XXX") so the shell is visually complete and end-to-end testable before
any feature page lands. ADR 0009 captures the navigation choice; ADR 0010
captures the MVVM toolkit choice (CommunityToolkit.Mvvm) that this task also
lands.

## What

Replace the placeholder content area of `Views\MainWindow.xaml` with a wpfui
`ui:NavigationView` plus four stub pages, add the CommunityToolkit.Mvvm
package, and add a thin persistent status footer below the navigation area.

### Shell

- `MainWindow.xaml`'s row-1 grid (currently the placeholder Mica splash with
  sidebar `StackPanel`) is replaced by a `ui:NavigationView` plus a status
  footer (see "Status footer" section below):
  - `PaneDisplayMode="Left"`, `IsBackButtonVisible="Collapsed"`,
    `IsPaneToggleVisible="False"` (sidebar is always shown — matches
    WhisperHeim and the styleguide).
  - `OpenPaneLength="220"` (matches the styleguide's "~220 px wide" rule).
  - `MenuItems`: four `<ui:NavigationViewItem>` entries, in this order:
    1. **Speak** — icon `Mic24` — `TargetPageType="{x:Type pages:SpeakPage}"`.
    2. **Voices** — icon `PersonVoice24` (or closest match in Fluent System
       Icons) — `TargetPageType="{x:Type pages:VoicesPage}"`.
    3. **Settings** — icon `Settings24` —
       `TargetPageType="{x:Type pages:SettingsPage}"`.
    4. **About** — icon `Info24` — `TargetPageType="{x:Type pages:AboutPage}"`.
  - The brand mark + wordmark sit in the `NavigationView.PaneHeader` slot
    (the styleguide-canonical "top of sidebar" position). Reuse the existing
    inline `Viewbox`/`Canvas` brand-mark geometry from `MainWindow.xaml`
    (see "Brand mark" worker note below).
- `NavigationView.SetPageService(IPageService)` is called from
  `MainWindow.xaml.cs` once the host's `IServiceProvider` is wired in (the
  service is constructed in `EntryPoint.cs` and passed to `MainWindow` —
  see "DI / page resolution" below).
- The default landing page is **Speak** — set via `IsSelected="True"` on the
  Speak `NavigationViewItem`. wpfui handles the initial navigation from the
  selected item without an explicit `Navigate(...)` call, but if behaviour
  diverges in 3.1.x, fall back to calling `NavigationView.Navigate(typeof(SpeakPage))`
  on `MainWindow.Loaded`.

### Page abstraction

- New folder `src\Mockingbird\Views\Pages\` containing four `Page` subclasses
  (real WPF `System.Windows.Controls.Page`, not `UserControl` — the `Page`
  type is what `ui:NavigationView` navigates to and what `IPageService`
  resolves):
  - `SpeakPage.xaml(.cs)`
  - `VoicesPage.xaml(.cs)`
  - `SettingsPage.xaml(.cs)`
  - `AboutPage.xaml(.cs)`
- Each page implements **both**:
  - `Wpf.Ui.Controls.INavigableView<TViewModel>` — exposes the typed
    view-model property (the wpfui pattern for DI-friendly pages).
  - `Wpf.Ui.Controls.INavigationAware` — exposes `OnNavigatedTo()` and
    `OnNavigatedFrom()` (the actual lifecycle hooks in wpfui 3.x; `OnNavigatedTo`
    is **NOT** on `INavigableView<T>`).
- Stub content for this task: a single centred `TextBlock` per page —
  - SpeakPage: `"Speak — coming with main-013."`
  - VoicesPage: `"Voices — coming with main-014."`
  - SettingsPage: `"Settings — coming with main-016."`
  - AboutPage: `"About — coming with main-017."`
- Each page has a placeholder view-model class
  (`partial class SpeakPageViewModel : ObservableObject { }` etc.) — empty
  for v1, filled by the feature tasks. They exist so `INavigableView<T>` has
  a real generic argument and so feature tasks have a file to extend.
- Each page logs a Serilog Information event when its `OnNavigatedTo()` fires
  (`"Navigated to {PageName}"`) — proves the lifecycle hook is reachable for
  feature tasks.

### MVVM toolkit (new — captured by ADR 0010)

- Add `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />`
  to `Mockingbird.csproj`.
- View-models derive from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`
  (or are declared `partial` and decorated with `[ObservableObject]`).
- The convention for **all** mockingbird view-models, starting with the four
  placeholder VMs created here:
  - Bindable fields: `[ObservableProperty] private T _name;` →
    generator produces the public property + change-notification.
  - Commands: `[RelayCommand] private void DoX()` (or `private async Task DoXAsync()`)
    → generator produces the matching `IRelayCommand` / `IAsyncRelayCommand`.
  - Cross-property dependencies: `[NotifyPropertyChangedFor(nameof(Other))]`
    or `[NotifyCanExecuteChangedFor(nameof(SomeCommand))]`.
- No hand-rolled `ObservableBase` or `RelayCommand` helpers — the toolkit
  replaces them.

### Status footer (resolves Q4 — path A)

A thin persistent footer along the bottom of the window, below the
`NavigationView`, surfaces engine + transport health so the user keeps the
"is mockingbird healthy?" signal that the splash provides today.

- Layout: a small `Border` (height ~28 px) anchored at the bottom of the
  window's row-1 (the existing `MainWindow.xaml` outer Grid gets a third row
  for the footer). Subtle background (`{DynamicResource ControlFillColorDefaultBrush}`),
  thin top border, padded text — Mica-friendly, doesn't fight the window
  chrome.
- Content: a single left-aligned `TextBlock` showing
  `"HTTP {host}:{port}  •  Engine: {state}"`, e.g.
  `"HTTP 127.0.0.1:7223  •  Engine: pocket-tts"` or
  `"HTTP 127.0.0.1:7223  •  Engine: starting"`.
- Backed by a tiny `EngineStatusViewModel` (in
  `src\Mockingbird\ViewModels\EngineStatusViewModel.cs`) that exposes
  `[ObservableProperty] private string _httpEndpoint;` and
  `[ObservableProperty] private string _engineState;` and subscribes to the
  same signals the existing `MainWindow.xaml.cs` already wires in (sidecar
  state-change events from `SidecarHost`, plus the bound `SpeakServer`
  endpoint).
- Lifetime: registered as singleton in DI; `MainWindow.xaml.cs`
  constructor-injects it and the footer's `DataContext` is set to it.
- main-017 (About page) will eventually surface a richer engine-status
  panel; the footer can stay as the always-visible glance, or main-017 can
  decide to remove it. Out of scope for this task.

### DI / page resolution

- Use **wpfui's built-in `IPageService` + `INavigationService`** (no rolled
  `PageNavigationService`). Reasoning: they exist in 3.1.1, integrate
  natively with `NavigationView` via `NavigationView.SetPageService(...)`,
  and resolve through `IServiceProvider` via a thin adapter.
- `src\Mockingbird\Services\Navigation\PageService.cs` — a small
  `IPageService` implementation that delegates to `IServiceProvider` (one
  method, `T GetPage<T>() where T : class => (T)_provider.GetRequiredService(typeof(T))`).
- In `EntryPoint.cs`'s `ConfigureServices`:
  - Each page registered as **transient** so navigation always gets a fresh
    instance (matches wpfui's expectation; matches main-013/014/016/017
    constructor-injection model).
  - `services.AddSingleton<IPageService, PageService>();`
  - `services.AddSingleton<INavigationService, NavigationService>();`
    (wpfui's concrete `NavigationService` implements the interface.)
  - `services.AddSingleton<EngineStatusViewModel>();` for the status footer.
  - Each page's view-model registered as transient too (sibling to the page).
- `MainWindow` constructor takes `IPageService` and `EngineStatusViewModel`
  (alongside the existing `SpeakQueue`, `Action exitAction`);
  `MainWindow.xaml.cs` calls `RootNavigation.SetPageService(pageService)`
  in `OnInitialized` (or equivalent) so pages resolve through DI.
- For **this** task, page constructors take only an `ILogger<TPage>` —
  no feature dependencies yet. The feature tasks each add their own
  service dependencies through normal constructor injection.

### Other

- The placeholder splash artwork (centre logo + tagline) is removed — the
  About page (main-017) is where it belongs going forward. The status text
  it carried (`"HTTP listening..."`) moves to the new footer.
- The brand mark in the sidebar pane header is preserved. **Worker decides**
  whether to extract a `BrandMarkView` UserControl now or leave the geometry
  inline duplicated; main-017's About page will revisit if not extracted
  here. Either way works for this task — the only acceptance criterion is
  "brand mark appears at the top of the sidebar pane".
- The window's `Title="Mockingbird — Local voices for Claude Code"` is
  unchanged.
- The `ui:TitleBar` text stays **static** (`<ui:TitleBar Title="Mockingbird" />`).
  Active page is already obvious from the sidebar selection; dynamic
  per-page title binding adds plumbing for marginal value and matches
  WhisperHeim's static titlebar pattern.
- The tray menu (`Wpf.Ui.Tray.Controls.NotifyIcon` with Show / Stop / Exit) is
  unchanged.

## Acceptance criteria

- [ ] Running `mockingbird.exe` shows a `ui:NavigationView` with four sidebar
  entries (Speak, Voices, Settings, About), in that order, each with a Fluent
  icon and label.
- [ ] **Speak is the active page** when the window first opens (no extra
  click required).
- [ ] Clicking each sidebar entry navigates to the corresponding stub page
  with its "coming with main-XXX" text.
- [ ] Sidebar pane is the styleguide-canonical ~220 px wide.
- [ ] Each page class implements **both** `INavigableView<TViewModel>` and
  `INavigationAware`. The `OnNavigatedTo()` override on each page logs a
  Serilog Information event identifying the page (proves the lifecycle hook
  is reachable for feature tasks).
- [ ] The brand mark (speaking-person silhouette) appears at the top of the
  sidebar pane (in `NavigationView.PaneHeader`).
- [ ] `Mockingbird.csproj` references `CommunityToolkit.Mvvm` 8.x. All four
  placeholder `*PageViewModel` classes derive from `ObservableObject` (or
  are decorated with `[ObservableObject]`).
- [ ] Pages resolve through DI via `IPageService`. Each page class has
  `ILogger<TPage>` constructor-injected and the binding works with no
  manual `new SpeakPage()` calls anywhere in the codebase.
- [ ] A persistent status footer is visible at the bottom of the window
  showing `"HTTP {host}:{port}  •  Engine: {state}"`. The engine field
  updates live as `SidecarHost` reports state changes (e.g., `starting`
  → `running` → `failed`).
- [ ] No regression in the existing skeleton: HTTP server still listens on
  127.0.0.1:7223; tray menu still shows Show / Stop / Exit; double-tap LCtrl
  still drains the queue; closing the window still hides to tray.
- [ ] Build clean: `dotnet build mockingbird.sln -c Debug` produces 0 errors,
  0 warnings.
- [ ] Visual matches the styleguide — Mica backdrop unchanged, Fluent
  controls, Segoe UI Variable.

## Notes

- **Decisions resolved during refinement (2026-05-01, two-pass + user resolution):**
  - **Q1 wpfui 3.x lifecycle hook.** In wpfui 3.1.1, `OnNavigatedTo()` /
    `OnNavigatedFrom()` live on **`INavigationAware`**, NOT on
    `INavigableView<T>`. `INavigableView<T>` only exposes the typed
    `ViewModel` property. **Pages implement both interfaces.**
  - **Q2 Page service.** Use wpfui's built-in `IPageService` +
    `INavigationService`. A thin `PageService` adapter delegates to
    `IServiceProvider`. No rolled `PageNavigationService`.
  - **Q3 MVVM toolkit.** **`CommunityToolkit.Mvvm`** (option a). Source-gen
    attributes (`[ObservableProperty]`, `[RelayCommand]`) for all VMs.
    Captured in **ADR 0010**. Diverges from WhisperHeim's hand-rolled
    convention; the divergence is scoped to the MVVM-toolkit slot only.
    The user picked the more-maintainable option over WhisperHeim parity.
  - **Q4 Engine-status interim visibility.** **Path A** — ship a thin
    persistent status footer in this task. No signal regression between
    main-020 and main-017. The footer is a natural always-visible glance;
    main-017's About page will surface a richer panel and may or may not
    remove the footer at that point.
  - **Q5 Brand mark extraction.** **Worker time.** Inline duplication or
    extracted UserControl, both fine. main-017 (About) will revisit if
    needed.
  - **Q6 TitleBar text.** **Static** (`Title="Mockingbird"`). Active page
    is already visible in sidebar selection; dynamic binding adds plumbing
    for marginal value.
- **ADR 0009** (`.agenthoff/knowledge/decisions/0009-navigation-shell-wpfui.md`)
  captures the wpfui-NavigationView choice and the rationale for splitting
  this out of main-013.
- **ADR 0010** (`.agenthoff/knowledge/decisions/0010-mvvm-via-inotifypropertychanged.md`)
  captures the choice of `CommunityToolkit.Mvvm`. Filename retains its
  original slug for stability; the title and content reflect the final
  decision.
- **Cross-task editorial fix (applied during this refinement):** main-013's
  spec previously named `INavigableView<SpeakPageViewModel>.OnNavigatedTo()`.
  In wpfui 3.x that method lives on `INavigationAware`, not
  `INavigableView<T>`. main-013 has been edited to refer to
  `INavigationAware.OnNavigatedTo()` (intent unchanged; interface name
  corrected).
- **Architectural divergence from WhisperHeim — flag for the worker:**
  WhisperHeim, the explicitly-cited design ancestor (ADR 0001, styleguide
  "matches WhisperHeim's shell"), does NOT use `ui:NavigationView`,
  `INavigableView<T>`, `IPageService`, or WPF `Page`. Its shell is hand-rolled
  `ListBox` + `ContentPresenter` with `UserControl` pages and a
  `NavigateTo(string)` switch. Mockingbird deliberately diverges per ADR 0009
  — for theme-aware transitions, accessibility, and Fluent control parity.
  The worker **must not copy-paste WhisperHeim's nav code**; the patterns
  are related but not interchangeable. WhisperHeim's `Loaded += OnPageLoaded`
  is workable as a fallback if `INavigationAware.OnNavigatedTo()` proves
  flaky during implementation, but `OnNavigatedTo` is the chosen primary
  hook.
- **Reference: `docs/styleguide.md`** § Inherited from WhisperHeim
  ("Sidebar nav layout"), § Page set.
- **Worker tip:** wpfui 3.1.x property names are stable for the primitives
  used here (`PaneDisplayMode`, `OpenPaneLength`, `IsBackButtonVisible`,
  `IsPaneToggleVisible`, `MenuItems`, `TargetPageType`, `IsSelected`). If
  any property name collides, fall back to the closest equivalent and note
  the divergence in this task's outcome block.
- This task **does not** ship feature content for any page. Resist the
  temptation to drop "real" Speak / Voices / Settings / About content here —
  that work belongs to main-013 / main-014 / main-016 / main-017 respectively.

## Promotion rationale

This task was previously promoted directly to `todo/`, dropped back to
`backlog/` after a second-pass refinement surfaced an unanswered product
question (Q4) and pending tactical choices (Q1, Q2, Q3, Q5, Q6), and is
now re-promoted to `todo/` after the user resolved all six in conversation:

- Q1 → both `INavigableView<T>` and `INavigationAware` (lifecycle on the latter).
- Q2 → wpfui's `IPageService` + `INavigationService` with thin `PageService` adapter.
- Q3 → `CommunityToolkit.Mvvm` (override on prior orchestrator pass; ADR 0010
  rewritten).
- Q4 → path A: persistent status footer.
- Q5 → worker decides extraction.
- Q6 → static TitleBar.

main-013's editorial fix (`INavigableView<T>` → `INavigationAware` for the
`OnNavigatedTo` reference) has been applied. Dependency `main-010`
(styleguide) is `done` and the gate is OPEN. Scope is concrete and
self-contained. Ready for a worker.

## Outcome

Navigation shell shipped. `MainWindow.xaml` now hosts a wpfui
`ui:NavigationView` (`PaneDisplayMode="Left"`, `OpenPaneLength="220"`,
`IsBackButtonVisible="Collapsed"`, `IsPaneToggleVisible="False"`) with the
canonical four-page set Speak / Voices / Settings / About — each a real
`System.Windows.Controls.Page` subclass implementing both
`Wpf.Ui.Controls.INavigableView<TViewModel>` and
`Wpf.Ui.Controls.INavigationAware`, logging a Serilog Information event
on `OnNavigatedTo`. The brand mark sits in the `NavigationView.PaneHeader`
slot — inline duplication of the existing `Viewbox`/`Canvas` geometry
(Q5: worker chose not to extract a UserControl; main-017 may revisit).

`CommunityToolkit.Mvvm` 8.x is wired into `Mockingbird.csproj`; the four
placeholder `*PageViewModel` classes derive from `ObservableObject` and
sit in `src\Mockingbird\ViewModels\Pages\`. `EngineStatusViewModel` (in
`src\Mockingbird\ViewModels\`) backs a thin always-visible status footer
showing `HTTP {host}:{port}  •  Engine: {state}` — bound live to a new
`SidecarHost.StateChanged` event so the engine field tracks
`starting → running → restarting → failed → stopping` transitions. The
stub-engine path (`MOCKINGBIRD_USE_STUB_ENGINE=1`) reads `stub`.

Pages resolve through DI via wpfui's built-in `IPageService`. A thin
`PageService` adapter (`src\Mockingbird\Services\Navigation\PageService.cs`)
delegates to the host's `IServiceProvider`. `MainWindow.xaml.cs` calls
`RootNavigation.SetPageService(...)` in its constructor and explicitly
`Navigate(typeof(SpeakPage))` in `Loaded` (worker tip: wpfui 3.1.x does not
expose `IsSelected="True"` as a direct XAML property on `NavigationViewItem`,
so the styleguide-canonical "Speak is the default landing page" is achieved
through the explicit `Navigate` call rather than an `IsSelected` attribute).

`SpeakServer` exposes `Host` / `Port` getters so the footer view-model can
display the bound endpoint. `SidecarHost` raises a new `StateChanged` event
with a fresh `SidecarStatus` snapshot whenever its internal lifecycle bucket
flips; the footer VM marshals to the WPF dispatcher before mutating bound
properties.

Build clean: `dotnet build mockingbird.sln -c Debug` → 0 warnings, 0 errors.

Key files:
- `src\Mockingbird\Views\MainWindow.xaml(.cs)` — shell + footer
- `src\Mockingbird\Views\Pages\{Speak,Voices,Settings,About}Page.xaml(.cs)`
- `src\Mockingbird\ViewModels\EngineStatusViewModel.cs`
- `src\Mockingbird\ViewModels\Pages\{Speak,Voices,Settings,About}PageViewModel.cs`
- `src\Mockingbird\Services\Navigation\PageService.cs`
- `src\Mockingbird\Services\Tts\SidecarHost.cs` — added `StateChanged` event
- `src\Mockingbird\Services\Http\SpeakServer.cs` — exposed `Host` / `Port`
- `src\Mockingbird\EntryPoint.cs` — DI registrations
- `src\Mockingbird\Mockingbird.csproj` — `CommunityToolkit.Mvvm` reference

ADRs 0009 (navigation shell) and 0010 (CommunityToolkit.Mvvm) were already
drafted on this branch and accepted as-is — review confirmed they describe
exactly what shipped. No new ADRs needed.
