---
id: 0009
title: Page navigation via wpfui NavigationView with INavigableView pages
scope: main
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-013, main-020]
related_research: []
---

# ADR 0009: Page navigation via wpfui NavigationView with INavigableView pages

## Context
The walking skeleton (main-009) shipped a placeholder `MainWindow.xaml` with a
hard-coded two-column `Grid` (a 220px sidebar `StackPanel` plus a centre splash).
There is no page abstraction, no routing, no concept of the four-page set the
styleguide canonicalised (Speak, Voices, Settings, About).

When refining main-013 (Speak page), it became clear that delivering "Speak is
the default landing page" requires building the navigation shell first — and
that shell is shared infrastructure for main-014 (Voices), main-016 (Settings),
and main-017 (About). Carrying it inside main-013 would bury 200+ lines of
plumbing under a single feature task, and would force whichever page-task
landed first to own a primitive every other page-task depends on.

The styleguide already names the primitive: WPF UI's `ui:NavigationView` with
the standard left-sidebar layout (~220 px) is part of the "inherited from
WhisperHeim" baseline (`docs/styleguide.md` § Inherited from WhisperHeim:
"Sidebar nav layout — single window, left vertical sidebar (~220 px wide) with
icon + label rows, content area on the right. Matches WhisperHeim's shell").

Three options were on the table:

1. **Embed nav inside main-013.** Speak page task owns shell + Speak content.
   Subsequent page tasks would tack their pages onto an existing shell.
2. **Roll our own** sidebar + Frame using vanilla WPF. More code; loses wpfui
   theming + transitions; contradicts the styleguide's "Fluent controls — WPF UI
   throughout" rule.
3. **Split out a navigation-shell task** (`main-020`) that delivers the
   `ui:NavigationView` skeleton plus stub pages for Speak/Voices/Settings/About.
   Subsequent page tasks each replace one stub with real content.

## Decision
**Option 3.** A dedicated navigation-shell task (`main-020-navigation-shell`)
delivers the page-routing primitive ahead of all four page tasks. The shell
uses **`Wpf.Ui.Controls.NavigationView`** with `PaneDisplayMode="Left"`,
populated from `MenuItems` referencing four page types that each implement
`Wpf.Ui.Controls.INavigableView<TViewModel>`.

Concretely:

- `MainWindow.xaml` hosts a `<ui:NavigationView>` whose `MenuItems` are four
  `<ui:NavigationViewItem>` entries (Speak / Voices / Settings / About) with
  Fluent system icons and a `TargetPageType` per item.
- A `Pages\` folder holds `SpeakPage`, `VoicesPage`, `SettingsPage`, `AboutPage`,
  each a `Wpf.Ui.Controls.NavigationViewContentPresenter`-compatible `Page`
  subclass implementing `INavigableView<TViewModel>`.
- A small `PageNavigationService` (or direct DI registration of the page types)
  resolves pages from the host's DI container so each page can take its
  dependencies (`SpeakService`, `VoiceCatalog`, etc.) via constructor injection.
- The "page is now visible" event the page tasks rely on is the page's
  `OnNavigatedTo()` override (from `INavigableView<T>`) plus the page's own
  `Loaded` event as a fallback. main-013, main-014, and main-017 hook one of
  these for state refresh on entry.
- The default landing page is **Speak** (per styleguide).
- Stub pages (`VoicesPage`, `SettingsPage`, `AboutPage`) ship with a single
  centred "Coming soon — see main-014/016/017" label so the shell is visually
  complete before the page tasks land.

## Consequences

### Positive
- One canonical primitive for all four pages. The shell task is the single
  place "how does navigation work?" is answered.
- Page tasks (main-013/014/016/017) shrink to "build this page's content +
  view-model". They no longer carry shell plumbing.
- The dependency graph becomes explicit: page tasks `depends_on: [main-020]`.
  Adding a fifth page later is a single sidebar entry, not a rebuild.
- WPF UI's `NavigationView` ships with theme-aware transitions, breadcrumb /
  pane behaviour, and accessibility wiring — matching the "feels like a
  first-party Windows app" success criterion in the vision.
- The shell can ship with stub pages and still be visually testable end-to-end
  before any feature page is built — a real continuous-feedback loop instead
  of "everything is dark until main-013 lands".

### Negative
- Introduces one extra task in the page-set sequence (main-020 must ship before
  main-013/014/016/017). On a one-developer project this is essentially
  bookkeeping; on the order of a single afternoon's work.
- `INavigableView<T>` is a wpfui-specific contract. If wpfui is ever swapped
  out, the page abstraction migrates with it. Acceptable: ADR 0001 already
  commits to wpfui as the control library, so this isn't new coupling.

### Neutral
- The `PageNavigationService` is thin (dozens of lines, mostly a `Type → Page`
  resolver hooked into DI). It does not warrant its own ADR.
- main-013's open question about the canonical "page-shown" event resolves to
  `INavigableView<T>.OnNavigatedTo()`. main-014 and main-017 inherit this.

## Alternatives considered

- **Embed nav inside main-013** (option 1) — rejected: buries shared plumbing
  under a single feature task, and contorts the dependency graph (every other
  page task transitively depending on whichever feature task happened to ship
  first). Splits Marco's "smallest sensible task" preference into a giant task
  and three stuck-behind-it tasks.
- **Roll-your-own sidebar + Frame** (option 2) — rejected: loses wpfui's theme,
  transitions, and Fluent control set; contradicts the styleguide's rule of
  using WPF UI controls throughout. No upside given wpfui is already a
  baseline dependency.
- **Single window with conditional content + a `TabControl`** — rejected: tab
  controls don't match the WhisperHeim sidebar paradigm the styleguide
  inherited. Sidebar-with-icons is the correct Fluent pattern for a four-page
  settings-style app.

## References
- Styleguide: `docs/styleguide.md` § Inherited from WhisperHeim ("Sidebar nav
  layout"), § Page set (canonical four pages).
- ADR 0001: `.agentheim/knowledge/decisions/0001-stack-net9-wpf-x64.md`
  (commits to wpfui as the control library).
- BC README: `.agentheim/contexts/main/README.md` (UI / styleguide gate).
- WPF UI documentation: `Wpf.Ui.Controls.NavigationView`,
  `Wpf.Ui.Controls.INavigableView<T>`.
