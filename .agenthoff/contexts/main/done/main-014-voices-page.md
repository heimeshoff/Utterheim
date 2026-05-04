---
id: main-014
title: Voices page — voice library list with preview
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-04
commit:
depends_on: [main-010, main-011, main-020]
blocks: [main-015]
tags: [frontend, page, voice-library]
---

## Outcome

Replaced the main-020 Voices stub with the real read-only voice library
shell. Two grouped sections (Built-in first, Cloned second) under a
`FontWeight="Light"` page title, both inside a single `ScrollViewer` with
`SemiBold` section headers. Each row has a name + meta line, a Preview
button (`ui:Button` with `Play24`), and a `Speaker224` active-request
indicator that lights up when its voice is the current speak request.
Cloned section shows the inline empty-state copy in v1.

Per ADR 0014, Preview routes through `SpeakService.Enqueue($"Hello, this
is {DisplayName}.", voiceId)` — never directly to `ITtsEngine` or
`AudioPlayer`. The queue invariant (ADR 0007) and stop semantics (ADR
0004) are preserved end-to-end. The page consumes `VoiceCatalog`
exclusively; no `library.json` reads (deferred to main-015).

Engine state binds to `SidecarHost.StateChanged`:
`starting`/`restarting`/`notstarted` → centred ProgressRing placeholder;
`failed` → inline critical-style error banner pointing at About;
`running` → normal list. A page-VM subscription to
`SpeakService.StatusChanged` flips per-row `IsActiveRequest` flags via
the dispatcher. Re-entry-guarded refresh on `OnNavigatedTo` and
`VoiceCatalog.VoicesChanged` (the same event main-015 will fire on
save / delete).

Build clean (`dotnet build mockingbird.sln -c Debug` → 0 errors,
0 warnings). Interactive UI behaviours (preview latency, FIFO ordering
with concurrent Claude requests, stop-hotkey draining the preview) are
**not interactively re-tested** in this pass; the code paths are in
place per the spec and any regression will surface during the next
manual run.

Key files:

- `src/Mockingbird/Views/Pages/VoicesPage.xaml` — two-section list +
  loading / failed placeholders.
- `src/Mockingbird/Views/Pages/VoicesPage.xaml.cs` — INavigationAware
  hooks, dispatcher-marshalled subscriptions to VoicesChanged /
  StatusChanged / StateChanged.
- `src/Mockingbird/ViewModels/Pages/VoicesPageViewModel.cs` — page VM
  + nested `VoiceRowViewModel` (per the spec's worker tip — single
  file, no separate row file).

ADR 0014 was already at `status: accepted` from the earlier refinement
commit (3949301); no flip needed during execution.

## Why

Once voice cloning lands (main-015) the user will accumulate voice profiles
and needs a place to browse and audition them. Even before cloning, the page
makes the eight pocket-tts built-ins discoverable — the user can hear "alba"
before assigning it to a Claude session. Mirrors WhisperHeim's TTS section B
(Voices), specifically the "Custom Voices list" component plus the built-in
voices view.

This task delivers the **read-only list + preview** shell. Cloned-voice rows
are inert in v1 (the catalog returns built-ins only) but the page is
structured so main-015 can fold in cloned voices via the existing
`VoiceCatalog` seam without touching this page's code. The **delete affordance
moves to main-015** where the cloned voices it operates on actually exist
(see "Scope adjustment" below).

## What

Replace the `VoicesPage` stub from main-020 with the real voice-library
content, reusing the navigation shell, the page abstraction
(`INavigableView<VoicesPageViewModel>` for the typed view-model property,
`INavigationAware` for `OnNavigatedTo()` / `OnNavigatedFrom()`), and the
`CommunityToolkit.Mvvm` MVVM toolkit (per ADR 0010 / main-020 conventions).

### Layout (per styleguide § Reusable component map → "Voices list with preview + delete affordances")

A `Grid` with two rows: a compact header row and a scrollable list that fills
the remaining vertical space. Outer `Margin="16"` matching the Speak page
(main-013) for visual consistency.

```xml
<Grid Margin="16">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- 1. header / section title -->
    <RowDefinition Height="*" />     <!-- 2. scrollable voices list -->
  </Grid.RowDefinitions>
  ...
</Grid>
```

1. **Header** — `TextBlock` "Voices" with `FontWeight="Light"` (per styleguide
   typography rule), plus a small `TextBlock` showing the catalog count
   (e.g. "8 voices" — bound to `VoicesPageViewModel.TotalCount`).
2. **List** — wrapped in a `ScrollViewer`
   (`VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"`)
   so long libraries scroll inside the page rather than the page scrolling
   the window. Inside, two grouped sections (see "Grouping" below):
   - **Built-in voices** section header + an `ItemsControl` of voice rows.
   - **Cloned voices** section header + an `ItemsControl` of voice rows.

### Grouping (resolves Q2)

**Two sections, built-ins first** — not a flat list with badges and not a
filter/toggle. Rationale:

- The styleguide's reusable component map names this as "Voices list" — a
  single ordered list — and the WhisperHeim §6 Section B reference does the
  same. Two visual sections within one list keeps the canonical shape while
  giving the user a clear "what ships with the app vs what I made" mental
  separation.
- v1's voice count (8 built-ins + 0–~10 cloned in early use) is small enough
  that section headers are sufficient hierarchy. Filters / search are
  vision-deferred until ~15 voices. Don't build them now.
- Section headers are simple `TextBlock`s with `FontWeight="SemiBold"` —
  "Built-in" and "Cloned". When the cloned section is empty in v1, its
  header **and** an inline empty-state message ("No cloned voices yet —
  see main-015 to clone your own.") render so the user understands the
  layout.

The view-model exposes two `ObservableCollection<VoiceRowViewModel>`s
(`BuiltInVoices` and `ClonedVoices`) populated by partitioning
`VoiceCatalog.ListAsync()`'s result on `IsBuiltIn`. Keep the same
`VoiceDescriptor` type the catalog already returns; do **not** invent a
parallel cloned-voice DTO here.

### Voice row composition

Each row is a horizontal layout (`Grid` with three columns: name+meta
fills, preview button auto-width, status indicator auto-width) with
`Padding="8,12"` and a thin bottom border for separation:

- **Column 1 — name + meta**:
  - Voice display name in normal weight (e.g. `alba`).
  - A small `TextBlock` below in `Foreground="{DynamicResource TextFillColorSecondaryBrush}"`
    showing the engine + (cloned only) source + createdAt: `"pocket-tts"` for
    built-ins; `"pocket-tts • mic • 2026-05-04"` for cloned voices when they
    eventually appear via main-015.
- **Column 2 — Preview button**: `ui:Button` with icon `Play24`,
  `Appearance="Secondary"`, label "Preview". Bound to
  `VoiceRowViewModel.PreviewCommand`. `IsEnabled` is bound to
  `CanPreview` (false while engine state is not `running`, see Q5).
- **Column 3 — status indicator**: tiny dot / icon (e.g. `ui:SymbolIcon`
  with `Symbol="Speaker224"`) that lights up when **this** row's preview is
  the active speak request. Bound to `VoiceRowViewModel.IsActiveRequest`.
  When idle, hidden via `Visibility="Collapsed"`.

There is **no delete button** in v1 (see "Scope adjustment").

### Behaviour

#### Preview pathway (resolves Q1 — captured as ADR 0014)

Preview routes through **`SpeakService.Enqueue(cannedPhrase, voiceId)`** —
the same in-process seam main-013 wired up for the Speak page's Play
button and `POST /speak`. **Not** a direct `ITtsEngine.StreamAsync` call,
**not** a side `AudioPlayer` instance.

```csharp
// In VoiceRowViewModel.PreviewAsync():
var phrase = $"Hello, this is {DisplayName}.";
_speakService.Enqueue(phrase, VoiceId);
```

Why: ADR 0007 makes the queue the single arbiter of "what plays next."
A direct off-queue call from the Voices page would race Claude-driven
`/speak` requests for the output device and break the stop-hotkey
contract (ADR 0004). The audition-latency cost of going through the
queue (waiting for an active Claude utterance to finish before the
preview plays) is acceptable for v1 and is the same trade-off main-013
made for Play. Lanes / barge are deferred to v1.5 per ADR 0007.

Captured as **ADR 0014** at `.agenthoff/knowledge/decisions/0014-voices-page-preview-via-speak-queue.md`.

#### Catalog refresh on page-shown (resolves Q3)

Hook `INavigationAware.OnNavigatedTo()` to refresh, and subscribe to
`VoiceCatalog.VoicesChanged` for live updates while the page is mounted.
Same pattern main-013 uses for the Speak page's voice picker:

1. On `OnNavigatedTo()`: `await viewModel.RefreshVoicesAsync()` →
   `VoiceCatalog.ListAsync()` → partition by `IsBuiltIn` → replace the
   two `ObservableCollection`s.
2. Subscribe to `VoiceCatalog.VoicesChanged` (with unsubscribe on
   `OnNavigatedFrom()` / `Unloaded`). When main-015 fires it on save,
   the page refreshes live without re-navigation.
3. Subscribe to `SpeakService.StatusChanged` (same lifecycle) so each
   row's `IsActiveRequest` indicator can flip on/off as preview
   requests transition through `synthesising → playing → idle`.

No file-watcher on `library.json`. The `VoiceCatalog` service is the
single source of truth; main-015 mutates it through normal method calls,
which fire `VoicesChanged`. File-watcher would be redundant and
fragile (atomic-write-then-rename per ADR 0005 already breaks naive
watchers).

#### Library reading owner (resolves Q4 — by deferral)

main-014 does **NOT** read `library.json`. It consumes
`VoiceCatalog.ListAsync()` exactly as main-013's voice picker does. v1's
`VoiceCatalog` returns built-ins from `ITtsEngine.ListVoicesAsync(...)`
only.

main-015 owns the cloned-voice surface end-to-end: it adds a
`VoiceLibraryService` (or augments `VoiceCatalog` directly) that reads
`library.json` per ADR 0005's schema (`{ id, name, engine,
pocketTtsVersion, source, createdAt, tags, samplePath }` per voice via
`meta.json`; `library.json` is the master index). When that lands, the
Voices page automatically shows the new rows because it consumes the
catalog, not the file. **Schema ratification belongs to main-015's
refinement**, not this task.

This factoring keeps main-014 small and avoids speculative file I/O for
an empty file.

#### Engine-state visibility (resolves Q5)

The Voices page shares the persistent status footer from main-020
(`HTTP {host}:{port} • Engine: {state}`) — no per-page status panel
here. In addition:

- **`starting` / `restarting`** — the catalog is empty until the engine
  is `running`; the page renders a centred placeholder
  (`ui:ProgressRing` + "Voice engine is starting...") in place of the
  list. Preview buttons would be disabled anyway since there are no
  rows.
- **`failed`** — the page renders an error banner above the list:
  "Voice engine failed to start. See the About page for details and
  retry." (main-017 will surface the richer panel and a retry
  control.) The catalog will be empty, so no rows render below.
- **`running`** — normal list. Preview buttons enabled per row.
- **`stopping`** — preview buttons disable (the sidecar is on its way
  out); already-enqueued previews drain via the existing queue
  shutdown path.

Bound via a `EngineState` property on `VoicesPageViewModel` that
subscribes to `SidecarHost.StateChanged` (same event the footer uses).
No new event plumbing needed.

**Per-row preview error UX**: deferred. If a preview request faults
mid-synthesis (sidecar dies between `Enqueue` and chunk delivery), the
existing `SpeakService` / `SpeakQueue` error path handles it — the row's
`IsActiveRequest` indicator flips off and the status footer reflects
the failed engine state. Adding per-row error toasts is scope creep
for v1; the footer + log file is sufficient signal.

#### Preview-while-something-is-playing (resolves Q6)

Same answer as the Speak page Play button (main-013 Q1): **FIFO, no
barge**. Per ADR 0007.

- Click Preview on row B while row A's preview is still playing →
  row B enqueues behind row A. Both rows show their `IsActiveRequest`
  indicator in turn as the queue advances.
- Click Preview while a Claude `/speak` request is mid-flight →
  enqueues behind it.
- Double-tap LCtrl during a preview → drains the queue per ADR 0004,
  same as any other request. The preview's row indicator flips off;
  the status footer briefly shows `stopped` then `idle`.

This is consistent across every entry point (Speak Play, Voices Preview,
HTTP `/speak`) because they all funnel through `SpeakService.Enqueue`.

#### Per-session voice routing UI (resolves Q8 — confirm out of scope)

**Out of scope for this task and for v1.** Per the BC README's "Claude
Code integration kit" section: voice routing is caller-side by design,
delivered via the `MOCKINGBIRD_VOICE` env var per terminal (main-019).
The Voices page does **not** absorb a per-session UI. If the user
later wants in-app routing, that's a separate feature with its own
decision task; surface it then, do not pre-build the surface here.

## Acceptance criteria

- [ ] Voices page is reachable from the sidebar nav (delivered by
  main-020; this task verifies it still holds after the stub is
  replaced).
- [ ] The page renders **two sections** in a single scrollable list,
  built-ins first then cloned voices, with `SemiBold` section
  headers labelled "Built-in" and "Cloned".
- [ ] When the cloned section is empty (always, in v1), the section
  shows an inline empty-state message: "No cloned voices yet — see
  main-015 to clone your own."
- [ ] All eight pocket-tts built-ins (`alba`, `marius`, `javert`,
  `jean`, `fantine`, `cosette`, `eponine`, `azelma`) appear in the
  built-in section once the engine reports them, each with a Preview
  button and the meta line `"pocket-tts"`.
- [ ] Clicking **Preview** on a row produces audible speech of
  `"Hello, this is {voiceName}."` in that voice with first-chunk
  latency ≤2 s warm (per the BC README first-chunk-latency budget;
  measured ~190 ms warm for the canned phrase, ~320 ms cold).
- [ ] Preview routes through **`SpeakService.Enqueue(...)`** — verified
  by inspecting the view-model: no direct calls to `ITtsEngine`,
  `AudioPlayer`, or `SpeakQueue` from this page. The call site is
  `_speakService.Enqueue(...)`. ADR 0014 governs.
- [ ] Clicking Preview on row B while row A's preview is still
  playing enqueues row B behind row A (FIFO, no barge). Both rows
  show their `IsActiveRequest` indicator in turn.
- [ ] Clicking Preview while a Claude `POST /speak` request is in
  flight enqueues the preview behind it (FIFO).
- [ ] Double-tap LCtrl during a preview drains the queue per ADR 0004:
  audio halts, queued previews discarded, footer shows `stopped`
  then `idle`. Identical effect to the tray menu's Stop and the
  `POST /stop` endpoint.
- [ ] The page subscribes to `VoiceCatalog.VoicesChanged` on
  `OnNavigatedTo()` and unsubscribes on `OnNavigatedFrom()`. When
  the event fires (e.g. main-015 saves a new voice in a future
  session), the list refreshes without re-navigation. Verifiable
  by hand-firing the event in a test harness.
- [ ] When `SidecarHost.State` is `starting` or `restarting`, the
  page renders a centred `ui:ProgressRing` + "Voice engine is
  starting..." in place of the list.
- [ ] When `SidecarHost.State` is `failed`, the page renders an
  inline error banner: "Voice engine failed to start. See the About
  page for details and retry."
- [ ] When `SidecarHost.State` flips to `running` from a starting /
  restarting state, the page automatically populates the list.
- [ ] No `library.json` is read by code shipped in this task. The
  page consumes `VoiceCatalog` exclusively. Grep verifiable
  (`VoiceLibraryPath` / `library.json` references in
  `Views\Pages\VoicesPage*` and `ViewModels\Pages\VoicesPage*`
  should be zero).
- [ ] Visual matches the styleguide — Mica backdrop, Fluent controls,
  Segoe UI Variable, no bespoke palette. Section headers use
  `FontWeight="SemiBold"`, page header uses `FontWeight="Light"`.
- [ ] Build clean: `dotnet build mockingbird.sln -c Debug` produces
  0 errors, 0 warnings.

## Notes

### Open questions resolved during refinement (2026-05-04)

- **Q1 Preview pathway** — through `SpeakService.Enqueue(...)`,
  identical to Speak page Play and `POST /speak`. ADR 0014 captures
  the rationale; the queue is the single arbiter of "what plays next"
  per ADRs 0004 / 0007 and a direct off-queue path would race
  Claude-driven utterances for the output device.
- **Q2 Built-ins vs cloned grouping** — two sections in a single
  scrollable list, built-ins first, `SemiBold` section headers. Empty
  cloned section shows an inline empty-state message in v1. No
  filter / search until ~15 voices (vision deferral).
- **Q3 Library refresh mechanism** — observable: page subscribes to
  `VoiceCatalog.VoicesChanged` on `OnNavigatedTo()`. main-015 fires
  the event on save / delete; this page picks up the change without
  re-navigation. Refresh on `OnNavigatedTo()` is the safety net. No
  file-watcher.
- **Q4 library.json reader / shape** — **deferred to main-015.**
  main-014 consumes `VoiceCatalog.ListAsync()` exclusively; v1's
  catalog returns engine built-ins only. ADR 0005 already defines
  the schema (`meta.json` per voice + `library.json` master index).
  main-015's refinement will pick the read/write owner (likely a new
  `VoiceLibraryService` composed into `VoiceCatalog`) and ratify the
  exact field set against pocket-tts's `export_voice` output.
- **Q5 Empty / error / loading states** — bound to
  `SidecarHost.State`. `starting`/`restarting` → progress ring +
  "starting" copy. `failed` → inline error banner pointing at About.
  `running` → normal list. Per-row preview error UX deferred to v1.5
  (footer + log file is sufficient signal in v1).
- **Q6 Preview while another preview / Claude is playing** —
  FIFO, no barge. Stop hotkey drains everything. Same as main-013
  Q1; consistent across every entry point because they share
  `SpeakService.Enqueue`.
- **Q7 Confirmation for delete** — **N/A in v1.** Delete affordance
  scope-moved to main-015 (see "Scope adjustment" below). main-015
  picks the confirmation pattern when it ships.
- **Q8 Per-session voice routing UI** — **out of scope.** v1 ships
  env-var-only routing per main-019 / BC README. Do not absorb the
  surface here.
- **Q9 Acceptance criteria sharpening** — replaced "within ~2 s"
  with the BC README's first-chunk-latency budget (≤2 s, measured
  ~190 ms warm / ~320 ms cold). The canned phrase is short, so warm
  should be well under 500 ms in practice.
- **Q10 Split decision** — **scope adjustment, not split.** Delete
  affordance moves to main-015 where the cloned voices it operates
  on actually exist. main-014 stays as one task: read-only list +
  preview. main-015's `depends_on: [main-010, main-014]` already
  reflects this; no new task created.

### Scope adjustment

The original task body listed delete in scope. **Delete is moved out**
because in v1 it operates on a row that does not exist (cloned voices
appear only after main-015 lands). Shipping a hidden / disabled delete
button against an empty section is dead code. main-015 owns:

- Adding cloned-voice rows to the catalog (via the new library reader).
- Adding the per-row delete button to cloned voices only.
- Choosing the confirmation pattern (modal / inline-toast-with-undo /
  inline-confirm) per the styleguide.
- Wiring delete to remove the row, the `meta.json`, the
  `profile.safetensors`, and the `library.json` entry.

main-014 ships the page shell that main-015 fills in. The dependency
direction (`main-015 depends_on main-014`) is unchanged.

### ADRs that govern this task

- **ADR 0014** —
  `.agenthoff/knowledge/decisions/0014-voices-page-preview-via-speak-queue.md` —
  preview routes through `SpeakService.Enqueue` (the single queue
  arbiter). **New, drafted as part of this refinement; status:
  proposed**, awaiting acceptance alongside this task's promotion.
- **ADR 0004** — stop drains queue (preview is halted by the same
  hotkey as any other request).
- **ADR 0007** — speak queue as `Channel<T>` (FIFO, lanes deferred).
  Preview is FIFO behind any active request.
- **ADR 0009** — navigation shell (this page implements both
  `INavigableView<VoicesPageViewModel>` and `INavigationAware`).
- **ADR 0010** — `CommunityToolkit.Mvvm` (use `[ObservableProperty]`
  / `[RelayCommand]` for the page VM and per-row VM).
- **ADR 0005** — voice persistence layout (governs the file shape
  main-015 will read; not consumed directly by this task).
- **ADR 0013** — streaming completion option (preview inherits the
  ~190 ms warm first-chunk latency this ADR delivers).

### References

- `docs/styleguide.md` § Reusable component map → "Voices list with
  preview + delete affordances".
- WhisperHeim `design.md` § 6 Section B.
- `.agenthoff/contexts/main/README.md` — UI / styleguide gate (open),
  Engine status (`GET /voices` returns the eight built-ins),
  first-chunk-latency budget.
- `src\Mockingbird\Services\Speak\VoiceCatalog.cs` — already exists
  (main-013); this task's view-model consumes it.
- `src\Mockingbird\Services\Speak\SpeakService.cs` — already exists
  (main-013); this task's view-model calls `Enqueue` and subscribes
  to `StatusChanged`.
- `src\Mockingbird\Services\Tts\SidecarHost.cs` — already exposes
  `StateChanged`; the page VM subscribes for the loading / error
  states.
- `src\Mockingbird\ViewModels\Pages\VoicesPageViewModel.cs` — empty
  stub from main-020; this task fills it.
- `src\Mockingbird\Views\Pages\VoicesPage.xaml(.cs)` — stub from
  main-020 (centred "Voices — coming with main-014." text); this
  task replaces the body.

### Out of scope (do not creep)

- Cloned-voice list rows (main-015).
- Delete affordance (main-015).
- Search / filter / tags (deferred per vision until ~15 voices).
- Per-session voice routing UI (env-var-only per main-019; no in-app
  surface in v1).
- Voice metadata editing (rename, retag) (no v1 task; surface if
  asked).
- Marketplace / sharing (explicit non-goal in vision).
- Per-row preview error toasts (footer + log file suffice in v1).

### Worker tips

- The preview row VM is small enough to be a record-with-properties or a
  trim `[ObservableObject] partial class`. Keep it inside
  `VoicesPageViewModel.cs` rather than a separate file unless it grows.
- `SpeakService.Enqueue` is synchronous (returns immediately with
  `(requestId, queuePosition)`). Wire `[RelayCommand]` on the
  preview method as a regular `void`/`Task` — no need for
  `IAsyncRelayCommand` on a fire-and-forget enqueue.
- For the per-row `IsActiveRequest` indicator: subscribe to
  `SpeakService.StatusChanged` once at the page-VM level, and on
  each event find the row whose `VoiceId` matches `status.VoiceId`
  and toggle its flag. Marshal to the WPF dispatcher before
  mutating bound properties (the existing `EngineStatusViewModel`
  has the same pattern — copy the dispatcher hop from there).
- The `VoiceCatalog.VoicesChanged` event currently fires once on first
  successful population (per main-013's spec). The page must tolerate
  receiving the event mid-`OnNavigatedTo()` (don't double-refresh) —
  guard with a "refreshing now?" flag.
