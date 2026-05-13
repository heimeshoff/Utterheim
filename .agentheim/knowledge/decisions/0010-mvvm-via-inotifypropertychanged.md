---
id: 0010
title: MVVM via CommunityToolkit.Mvvm source generators
scope: main
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-020, main-013, main-014, main-016, main-017]
related_research: []
---

# ADR 0010: MVVM via CommunityToolkit.Mvvm source generators

## Context

main-020 (navigation shell) introduces the four-page set — Speak, Voices,
Settings, About — each of which has bindable view-model state (text input,
voice selection, status labels, engine status, etc.). main-013's already-refined
spec names a `SpeakPageViewModel` with bindable properties (`Voices`,
`SelectedVoice`, `Text`) and commands (Play / Stop / Save). The feature page
tasks (main-013/014/016/017) all assume some kind of view-model lives next to
each page.

No MVVM library is currently referenced by `Mockingbird.csproj`. The choice has
to be made before the page tasks land, because it shapes view-model conventions
across every page: every VM looks the same way, or the codebase splits.

Three options were on the table:

1. **`CommunityToolkit.Mvvm` 8.x.** Microsoft-maintained source-generator-based
   MVVM library. `[ObservableObject]`, `[ObservableProperty]`, `[RelayCommand]`
   reduce a 5–6-line property to one decorated field. Adds ~150 KB and one
   transitive dep.

2. **Bare `INotifyPropertyChanged` + manual `ICommand`.** Hand-rolled
   `ObservableBase` + `RelayCommand` helpers (~80 LOC total). No new dependency.
   Verbose at the call-site (4–5 lines per property).

3. **Stay code-behind for v1.** Page state lives directly on the page class;
   no view-models. main-013's spec already contradicts this — it names a real
   `SpeakPageViewModel` with non-trivial state — so this option is awkward
   without rewriting that spec.

## Decision

**Option 1: `CommunityToolkit.Mvvm`.** Add the package to `Mockingbird.csproj`
and use its source-generator attributes on every view-model.

Concretely:

- `Mockingbird.csproj` gains `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />`.
- Page view-models (`SpeakPageViewModel`, `VoicesPageViewModel`, etc.) derive
  from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` (or are decorated
  with `[ObservableObject]` on a `partial class`).
- Bindable fields use `[ObservableProperty] private T _name;` — the generator
  produces the public property and change-notification.
- Commands use `[RelayCommand] private void/async Task DoX()` — the generator
  produces the matching `IRelayCommand`/`IAsyncRelayCommand` property the XAML
  binds to.
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` and
  `[NotifyCanExecuteChangedFor(nameof(SomeCommand))]` wire derived
  notifications so the VMs stay declarative.

main-020 ships only the package reference and the convention; no hand-rolled
helpers are needed. Subsequent page tasks pick up the convention by deriving
from `ObservableObject` (or applying `[ObservableObject]`).

## Consequences

### Positive

- **Less code per page.** A typical bindable property collapses from ~5 lines
  to 1. For a page like `SpeakPageViewModel` with three bindable properties
  and three commands that's ~30 lines of boilerplate eliminated. Across the
  five-VM set (Speak, Voices, Voice-cloning, Settings, About) the total
  saving is meaningful — ~150–200 lines of mechanical INPC plumbing the
  developer (and Claude, future-maintaining the app) doesn't have to read,
  review, or debug.
- **Modern WPF idiom.** `CommunityToolkit.Mvvm` is the current Microsoft-owned
  standard for WPF/WinUI MVVM. Pattern-matches to library docs, sample code,
  and AI-generated WPF the user might paste in.
- **Auto-wired derived notifications.** `[NotifyPropertyChangedFor]` and
  `[NotifyCanExecuteChangedFor]` capture cross-property dependencies
  declaratively. Hand-rolled INPC tends to drift on these — bindings stop
  updating when properties get added and the developer forgets to call
  `OnPropertyChanged` on the dependent.
- **Async commands free.** `[RelayCommand]` on an `async Task` method
  produces `IAsyncRelayCommand` with `IsRunning` and cancellation support
  out of the box — useful for the Speak page's Save (long-running render)
  and the Voice-cloning capture flow.
- **Microsoft-maintained, MIT-licensed, no transitive bloat.** ~150 KB,
  no native deps, ships out of the .NET Foundation. As safe a dependency
  as we get.

### Negative

- **One package dependency mockingbird wouldn't otherwise need.** Trades
  zero-dep purity for an ergonomic win. The dependency is small and
  high-pedigree, but it's still an extra moving part.
- **Diverges from WhisperHeim's convention.** WhisperHeim uses hand-rolled
  `INotifyPropertyChanged` (verified: no `CommunityToolkit.Mvvm` reference
  in its csproj; VMs in `Views/Pages/TranscriptsPage.xaml.cs` and
  `Services/Transcription/*` are hand-rolled). ADR 0001 committed to
  "shared aesthetic and stack with WhisperHeim". This ADR accepts a
  divergence on the *MVVM toolkit* slot specifically, while keeping the
  larger shared-stack commitment (WPF, .NET 9, wpfui, NAudio, Serilog,
  Microsoft.Extensions.Hosting). Aesthetic parity (Mica, Fluent, sidebar
  layout) is preserved; what differs is invisible to the user.
- **Source-gen at compile time.** Diagnostics for `[ObservableProperty]`
  errors point at generator output, not the source field. Modern IDE
  tooling (Rider, recent VS) handles this fine; CI logs may be slightly
  noisier on bad attribute use.

### Neutral

- The package adds one line to `Mockingbird.csproj`. If at some future
  point parity with WhisperHeim becomes more important than ergonomics
  (e.g., if WhisperHeim's VMs are unified into a shared library), removing
  the toolkit is a mechanical rewrite — every `[ObservableProperty]`
  field becomes a hand-rolled property. Reversible.
- Worker note: when wiring the Speak page (main-013), prefer
  `[ObservableProperty] private string _text = "";` over the older
  `private string _text; public string Text { get; set; } ...` pattern.
  The generator handles change-notification and `INotifyPropertyChanged`
  registration automatically.

## Alternatives considered

- **Bare `INotifyPropertyChanged`** (option 2) — rejected. Argument for it
  was WhisperHeim parity (ADR 0001) and zero new deps. Argument against it
  was the lifetime cost of hand-writing 5-line properties for 5+ VMs ×
  ~5 properties each, plus manual `RelayCommand` re-implementation. For a
  one-developer (plus AI-collaborator) project, ergonomics and standard
  patterns matter more than parity-of-toolkit-choice across two apps.
- **Code-behind only** (option 3) — rejected. main-013's spec explicitly
  names a `SpeakPageViewModel` with bindable state, lifecycle hooks, and
  service dependencies; the Voices, Voice-cloning, and Settings pages will
  have similar shapes. Collapsing all of that into page code-behind defeats
  the testability and separation that even a thin VM provides. The point
  of view-models here isn't ceremony; it's "the page knows nothing about
  the queue or the catalog directly".
- **`ReactiveUI`** — not seriously considered. Heavier dependency, very
  different paradigm, large for the scale of this project.

## References

- `CommunityToolkit.Mvvm` docs:
  https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/
- ADR 0001 — stack/parity commitment with WhisperHeim. This ADR accepts a
  scoped divergence on the MVVM toolkit while preserving the larger
  shared-stack and shared-aesthetic commitments.
- ADR 0009 — navigation shell choice (this ADR is the MVVM peer).
- main-013 (Speak page refinement) — the first VM consumer; the worker
  implementing main-013 should use `[ObservableProperty]` and
  `[RelayCommand]` from the start.
- WhisperHeim VM precedent (the convention this ADR knowingly diverges
  from): `c:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Views\Pages\TranscriptsPage.xaml.cs`
  uses hand-rolled `INotifyPropertyChanged`. Mockingbird does not.
