---
id: main-013
title: Speak page — primary daily-use UI
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-04
commit: a480d58
depends_on: [main-010, main-011, main-020]
blocks: []
tags: [frontend, page]
---

## Why

The Speak page is mockingbird's main daily surface — the place the user
interacts with directly when not driving via Claude Code hooks. It mirrors
WhisperHeim's TTS section A (Speak): a text box, a voice picker, play / stop /
save controls, and a status line. Without this page the tray app's main window
has no primary content; the navigation shell from main-020 lands with a
"Speak — coming with main-013" stub that this task replaces.

## What

Replace the `SpeakPage` stub from main-020 with the real Speak content,
reusing the navigation shell, the page abstraction
(`INavigableView<SpeakPageViewModel>` for the typed view-model property,
`INavigationAware` for `OnNavigatedTo()` / `OnNavigatedFrom()`), and the
`CommunityToolkit.Mvvm` MVVM toolkit the shell lands.

### Layout (per styleguide § Reusable component map → "Speak section composition")

A `Grid` with four rows: the **textbox is the dominant element and fills all
remaining vertical space**; the other three rows are compact (`Height="Auto"`).
The page uses an outer `Margin="16"` (or grid `Padding`) so the textbox is
never flush against the window chrome — small breathing room on every side
without wasting space.

Concretely:

```xml
<Grid Margin="16">
  <Grid.RowDefinitions>
    <RowDefinition Height="*" />     <!-- 1. text input — fills -->
    <RowDefinition Height="Auto" />  <!-- 2. voice picker -->
    <RowDefinition Height="Auto" />  <!-- 3. button row -->
    <RowDefinition Height="Auto" />  <!-- 4. status line -->
  </Grid.RowDefinitions>
  ...
</Grid>
```

Row gap is achieved with per-row `Margin="0,0,0,12"` (or a uniform `12px`
spacing convention from the styleguide).

1. **Multi-line text input** — Fluent `ui:TextBox` with `AcceptsReturn="True"`,
   `TextWrapping="Wrap"`,
   `VerticalAlignment="Stretch"`,
   `HorizontalAlignment="Stretch"`,
   `AcceptsTab="False"` (Tab moves focus, doesn't insert),
   `VerticalScrollBarVisibility="Auto"` (long pastes scroll inside the box,
   not the page),
   `MinHeight="200"` so the box is still substantial when the window is at
   its minimum size; **no `MaxHeight`** — it grows to fill the window.
   Placeholder text: "Type or paste something for Mockingbird to say...".
   The intent: the user can paste a multi-paragraph article, see most of it
   without scrolling on a normal-sized window, and edit comfortably.
2. **Voice picker** — `ui:ComboBox` (or `ComboBox` themed by wpfui) bound to
   `SpeakPageViewModel.Voices` (an `ObservableCollection<VoiceDescriptor>`).
   `HorizontalAlignment="Left"`, `MinWidth="200"` — does not stretch full
   width; the textbox is the page's hero, not the picker.
3. **Button row** — three `ui:Button` instances side-by-side
   (`StackPanel Orientation="Horizontal"`, child `Margin="0,0,8,0"`):
   - **Play** (`Mic24` or `Play24` icon, `Appearance="Primary"`).
   - **Stop** (`Stop24` icon, `Appearance="Secondary"`,
     `IsEnabled` bound to "playing OR queued").
   - **Save** (`Save24` icon, `Appearance="Secondary"`).
4. **Status line** — single `TextBlock` showing the current `SpeakStatus`
   label (see "Status-line state machine" below) plus the active voice name
   when playing or synthesising.

### Behaviour

#### Voice catalog (in-process seam — resolves Q4)

A new singleton service `Services\Speak\VoiceCatalog.cs` is added:

- `Task<IReadOnlyList<VoiceDescriptor>> ListAsync(CancellationToken ct)` —
  delegates to `ITtsEngine.ListVoicesAsync(ct)` for v1. (main-015 will
  augment this with cloned-voice rows from `library.json`.)
- `event EventHandler? VoicesChanged` — fired when the catalog mutates. v1
  fires it only on initial population; main-015 will fire on save/delete.
- `SpeakServer.MapGet("/voices", ...)` is refactored to consume
  `VoiceCatalog.ListAsync()` instead of calling `ITtsEngine` directly. This
  guarantees "page and HTTP endpoint use the same code path" structurally,
  and it's the seam main-015 needs to inject cloned voices into both surfaces
  in one place.
- Page consumes `VoiceCatalog` via constructor injection (transient page,
  singleton catalog). On `OnNavigatedTo()` (or on `VoicesChanged`), the page
  refreshes its picker.

#### Speak request seam (in-process — resolves Q2)

A new singleton service `Services\Speak\SpeakService.cs` is added:

- `(string requestId, int queuePosition) Enqueue(string text, string voiceId)`
  — owns request-construction (Guid generation, voice fallback, text
  validation) and calls `SpeakQueue.Enqueue(...)`. Returns the same shape the
  HTTP `/speak` endpoint returns.
- `int StopAndDrain()` — thin pass-through to `SpeakQueue.StopAndDrain()`.
  Mirrors the `/stop` endpoint.
- `Task RenderToFileAsync(string text, string voiceId, string filePath, CancellationToken ct)`
  — silent off-queue render: streams `ITtsEngine.StreamAsync(text, voiceId)`
  chunks into a `NAudio.Wave.WaveFileWriter` opened on `filePath` with the
  engine's `OutputFormat`. Used by Save (see Q5 below). **Does not** enqueue
  or play.
- `event EventHandler<SpeakStatus>? StatusChanged` — emits status transitions
  (see state machine below).
- `SpeakServer.MapPost("/speak", ...)` is refactored to call
  `SpeakService.Enqueue(...)` instead of constructing `SpeakRequest`s inline.
  `SpeakServer.MapPost("/stop", ...)` is refactored to call
  `SpeakService.StopAndDrain()`. Same code path as the page, structurally.

`SpeakQueue` itself does not change behaviour, but **two events** are added so
the service can surface status transitions:

- `event EventHandler<SpeakRequest>? RequestStarted` — fired in `ExecuteAsync`
  immediately after dequeuing and assigning `_current`.
- `event EventHandler<SpeakRequest>? RequestCompleted` — fired in the
  `finally` block after `_inFlight.TryRemove`.

`SpeakService` listens to these plus `AudioPlayer.IsPlaying` to compute its
status stream.

#### Play

- Disabled when `Text` is empty / whitespace-only or no voice is selected.
- On click: `SpeakService.Enqueue(viewModel.Text, viewModel.SelectedVoice.Id)`.
- Per ADR 0007 the queue is FIFO and unbounded — clicking Play while an
  HTTP-driven request is mid-flight enqueues *behind* it. **No barge.**
  This matches the vision's "decide after running sessions in anger" stance
  and ADR 0007's "multiple lanes deferred to v1.5". (Resolves Q1.)

#### Stop

- Calls `SpeakService.StopAndDrain()` — same effect as the `/stop` endpoint,
  the tray menu's Stop, and the double-tap LCtrl gesture (per ADR 0004).
- Visible whenever a request is in flight or queued; can be wired
  always-enabled with the call being a no-op when nothing is playing
  (cleaner UX, equivalent semantically).

#### Save (resolves Q5)

- On click: opens `Microsoft.Win32.SaveFileDialog` with `Filter="WAV|*.wav"`,
  `DefaultExt=".wav"`, suggested name `mockingbird-{voiceId}-{yyyyMMdd-HHmmss}.wav`.
- On confirm: calls
  `SpeakService.RenderToFileAsync(viewModel.Text, viewModel.SelectedVoice.Id, dialog.FileName, ct)`
  with a cancellation token tied to the page's lifetime.
- **Save renders the current textbox content from scratch** — it does NOT tee
  whatever just played. Rationale: the textbox is the source of truth; Save
  works whether or not the user has hit Play first; the second synthesis pass
  is ~1–2 s for typical phrases (acceptable). Avoids coupling the file-write
  path to the live audio pipeline.
- During the render, the Save button shows a small inline `ui:ProgressRing`
  and is disabled. On completion (or failure) the button returns to normal
  and the status line briefly shows "Saved to {filename}" or
  "Save failed — see logs".
- Save is independent of the speak queue — calling Save while a request is
  playing must NOT interrupt playback (uses `RenderToFileAsync`, which does
  not touch `SpeakQueue` or `AudioPlayer`).

#### Status-line state machine (resolves Q6)

Labels and transition rules:

| Label | Condition | Source signal |
|---|---|---|
| `idle` | No `_current` request and queue is empty | `SpeakQueue.RequestCompleted` (with empty queue), or initial state |
| `synthesising` | `_current` is set but `AudioPlayer.IsPlaying` is false | `SpeakQueue.RequestStarted` fires this label until first audio chunk hits the buffer |
| `playing` | `_current` is set and `AudioPlayer.IsPlaying` is true | `AudioPlayer` is polled / observed during the playback loop |
| `stopped` | Transient (~2000 ms) — set by `StopAndDrain()` then auto-clears to `idle` | Hooked into `SpeakService.StopAndDrain()` |

- `synthesising` may be invisibly brief because pocket-tts streams chunks
  with ~200 ms first-chunk latency. That's fine — the label is correct
  whenever it shows; if it's never visible because synthesis is fast, the
  user just sees `playing` directly.
- `stopped` auto-clears after 2000 ms via a `DispatcherTimer` started by
  `StopAndDrain()`. The 2000 ms value is documented in `SpeakService` and
  can move to settings later.
- The status line shows the active voice name when the label is
  `synthesising` or `playing`: e.g., `synthesising — alba`,
  `playing — alba`. `idle` and `stopped` show no voice.
- Status updates are pushed to the page via `SpeakService.StatusChanged` —
  the page subscribes on `OnNavigatedTo()` and unsubscribes on `OnNavigatedFrom()`
  (or on the `Unloaded` event) to avoid leaks.

#### Voice picker initial selection (resolves Q7)

The picker pre-selects the **configured default voice** read from a new
`UserSettings` service. v1 ships the *storage* of `DefaultVoiceId` here;
main-016 (Settings page) later adds the *UI* to mutate it.

- New service `Services\Settings\UserSettings.cs` (singleton):
  - Backing file: `%LOCALAPPDATA%\Mockingbird\settings.json` (alongside the
    existing path layout per ADR 0005). Read on startup, written atomically
    on mutation.
  - Single property in v1: `string? DefaultVoiceId`. Other settings slots
    (output device, start-minimised, etc.) are added by main-016 — the
    schema is forward-compatible (unknown JSON fields are ignored;
    `JsonSerializerOptions { ReadCommentHandling = Skip }`).
  - `event EventHandler<string?>? DefaultVoiceIdChanged` — fired on
    persistence; main-013 doesn't subscribe (page reads on `OnNavigatedTo`),
    but main-016 will.
- Voice-picker resolution order on `OnNavigatedTo()`:
  1. If `UserSettings.DefaultVoiceId` is set **and** that voice still exists
     in the catalog, select it.
  2. Else fall back to the first voice in the list (alphabetically `alba`
     for the eight pocket-tts built-ins).
- The user can override the picker for the current session by choosing a
  different voice in the dropdown — that does **not** mutate
  `DefaultVoiceId`. Persisting the per-page choice as the new default is
  main-016's job (a "make this the default" affordance, or an explicit
  Settings flow).
- Note: this resolution applies only to the **page's picker**. The HTTP
  `/speak` endpoint always uses the voice id in the request payload —
  callers (Claude Code) specify the voice explicitly, so there is no
  "default voice" path on the API side.

#### Refresh on page-shown (resolves Q8)

Hook `INavigationAware.OnNavigatedTo()` (the canonical "page is now visible"
event in wpfui 3.x; lives on `INavigationAware`, not `INavigableView<T>`).
The Speak page implements both interfaces — `INavigableView<SpeakPageViewModel>`
for the typed VM property, `INavigationAware` for the lifecycle hooks. On
entry:

1. `await viewModel.RefreshVoicesAsync()` — calls
   `VoiceCatalog.ListAsync()` and replaces the `ObservableCollection`.
2. Re-select the previously-selected voice if it still exists; otherwise
   fall back to the first voice in the list.
3. Subscribe to `VoiceCatalog.VoicesChanged` (with unsubscribe on
   `OnNavigatedFrom()` / `Unloaded`).

This means: launching mockingbird → cloning a new voice on the Voices page
(main-015) → returning to Speak → the new voice is in the picker without
restart.

## Acceptance criteria

- [ ] Speak page is the default landing page when the tray window opens (this
  is delivered by main-020; this task verifies it still holds after the stub
  is replaced with the real content).
- [ ] Multi-line text input accepts Enter for newlines (`AcceptsReturn=True`)
  and wraps long text without horizontal scroll.
- [ ] The text input is the dominant element on the page: it fills all
  vertical space not consumed by the voice picker, button row, and status
  line. There is a small uniform margin (~16 px) on all sides so the box
  is not flush against the window chrome, but it is otherwise as large as
  the window allows. Resizing the window grows/shrinks the textbox; the
  other rows stay fixed-height. A multi-paragraph paste fits without
  visually cramped scroll on a normal-sized window; longer content scrolls
  inside the textbox via its own scrollbar (page does not scroll).
- [ ] `MinHeight` on the textbox keeps it substantial (~200 px / ~10 visible
  lines) even at the window's minimum size — never collapsed to a one-line
  field.
- [ ] Voice picker shows the eight pocket-tts built-ins
  (`alba`, `marius`, `javert`, `jean`, `fantine`, `cosette`, `eponine`,
  `azelma`) once the engine reports them. Initial selection is
  `UserSettings.DefaultVoiceId` if set and present in the catalog;
  otherwise the alphabetical first entry (`alba`).
- [ ] `UserSettings` service persists `DefaultVoiceId` to
  `%LOCALAPPDATA%\Mockingbird\settings.json`. v1 has no UI to mutate it
  (that's main-016) but the file is read on startup if present, and
  unknown fields are tolerated for forward-compat with main-016.
- [ ] Voice picker refreshes whenever the page is navigated to (via
  `OnNavigatedTo()`); when a future task adds a new voice and fires
  `VoiceCatalog.VoicesChanged`, the picker updates without re-navigation.
- [ ] Clicking **Play** with non-empty text and a selected voice produces
  audible speech in that voice within ~2 s, **using the same in-process
  code path** as `POST /speak` — the call site is `SpeakService.Enqueue(...)`
  for both. **No HTTP loopback from the UI.**
- [ ] Clicking **Play** while another request (HTTP- or UI-driven) is
  playing enqueues the new request behind it (FIFO, no barge).
- [ ] Clicking **Stop** instantly halts current playback and drains the
  queue. Identical effect to the tray menu's Stop, the `POST /stop` endpoint,
  and the double-tap LCtrl gesture.
- [ ] Clicking **Save** opens a SaveFileDialog; on confirm, a `.wav` file is
  written at the chosen path containing the rendered audio of the **current
  textbox content** (not whatever last played). Save is functional even if
  Play has not been clicked first.
- [ ] Save does not interrupt an in-flight playback request (rendering
  happens off-queue via `SpeakService.RenderToFileAsync`).
- [ ] Status line shows `idle` initially, transitions through
  `synthesising — {voice}` and `playing — {voice}` during a request, shows
  `stopped` for ~2 s after Stop is clicked, and returns to `idle`. Verifiable
  with a long phrase (~200 words) where `synthesising` is observable before
  audio starts.
- [ ] `GET /voices` and the page's voice picker return identical lists
  (verified by hitting the endpoint while the page is open and comparing).
  Refactor of `SpeakServer` to consume `VoiceCatalog` is part of this task.
- [ ] `POST /speak` and the page's Play button enqueue requests through
  `SpeakService.Enqueue(...)` — verified by inspecting `SpeakServer.cs` no
  longer constructing `SpeakRequest` directly.
- [ ] Visual matches the styleguide — Mica backdrop, Fluent controls, Segoe
  UI Variable, no bespoke palette.
- [ ] Build clean: `dotnet build mockingbird.sln -c Debug` produces 0 errors,
  0 warnings.

## Notes

- **Open questions resolved during refinement:**
  - **Q1 FIFO vs barge** — FIFO. Per ADR 0007 (multiple-lanes deferred) and
    the vision's "decide after running sessions in anger". No code change to
    the queue.
  - **Q2 In-process seam** — `SpeakService` extracted (option a). Both the
    HTTP endpoint and the page VM consume it. Tactical note: the request
    construction logic (Guid generation, voice fallback, validation) moves
    out of `SpeakServer.cs` into `SpeakService`.
  - **Q3 Navigation shell split** — split out as **main-020** (in `todo/`).
    main-013 now `depends_on: [main-010, main-011, main-020]`. ADR 0009
    captures the rationale.
  - **Q4 Voice catalog seam** — `VoiceCatalog` extracted (option b).
    `SpeakServer.MapGet("/voices", ...)` refactored to consume it. Designed
    to absorb cloned voices in main-015 without further refactoring.
  - **Q5 Save semantics** — render-on-Save (option a). The textbox is the
    source of truth; double-synth penalty (~1–2 s for typical phrases) is
    acceptable. Save works without Play having been clicked.
  - **Q6 Status-line state machine** — four labels (idle / synthesising /
    playing / stopped). `stopped` is transient with a 2000 ms auto-clear.
    Active voice name appended to label when playing/synthesising. Driven by
    new `SpeakQueue.RequestStarted` / `RequestCompleted` events plus
    `AudioPlayer.IsPlaying`.
  - **Q7 Voice-picker initial selection** — read from `UserSettings.DefaultVoiceId`
    with `alba` fallback. **Storage** ships here (new
    `Services\Settings\UserSettings.cs` + `settings.json`); the **UI to mutate
    it** is main-016's responsibility. The HTTP `/speak` endpoint is
    unaffected — callers always specify a voice explicitly.
  - **Q8 Refresh on page-shown** — hook `INavigationAware.OnNavigatedTo()`
    (the canonical lifecycle event in wpfui 3.x; the page implements both
    `INavigableView<SpeakPageViewModel>` and `INavigationAware`, per
    main-020's spec). Editorial fix applied 2026-05-01: prior wording
    incorrectly named the method as living on `INavigableView<T>`.
- **ADRs that govern this task:**
  - **ADR 0009** — `.agenthoff/knowledge/decisions/0009-navigation-shell-wpfui.md`
    — rationale for splitting the navigation shell out of main-013 into
    main-020 and using `ui:NavigationView` + `INavigableView<T>`.
  - **ADR 0010** — `.agenthoff/knowledge/decisions/0010-mvvm-via-inotifypropertychanged.md`
    — formalises the `CommunityToolkit.Mvvm` choice this spec assumes.
    `SpeakPageViewModel` derives from `ObservableObject` (or `[ObservableObject] partial class`);
    use `[ObservableProperty]` for `Text` / `SelectedVoice` / `Voices` /
    `Status` and `[RelayCommand]` for Play / Stop / Save. Use
    `[NotifyCanExecuteChangedFor(nameof(PlayCommand))]` on `Text` and
    `SelectedVoice` so Play's `CanExecute` recomputes as the user types
    or picks a voice. `[RelayCommand]` on the async Save method gives
    `IsRunning` for the inline `ProgressRing` for free.
- **New tasks created:**
  - **main-020** — `.agenthoff/contexts/main/done/main-020-navigation-shell.md`
    — landed; the navigation shell exists with a Speak stub for this task to
    replace.
- **References:**
  - `docs/styleguide.md` § Reusable component map → "Speak section composition".
  - WhisperHeim `design.md` § 6 Section A.
  - ADR 0003 (HTTP transport), ADR 0004 (stop drains), ADR 0007
    (Channel queue), ADR 0009 (navigation shell).
  - Existing skeleton:
    - `src\Mockingbird\Services\Speak\SpeakQueue.cs` — needs two new events.
    - `src\Mockingbird\Services\Http\SpeakServer.cs` — refactor to use
      `SpeakService` + `VoiceCatalog`.
    - `src\Mockingbird\EntryPoint.cs` — register `SpeakService`,
      `VoiceCatalog`, and `UserSettings` as singletons in DI.
    - **New:** `src\Mockingbird\Services\Settings\UserSettings.cs` —
      typed wrapper over `%LOCALAPPDATA%\Mockingbird\settings.json`.
      Forward-compatible JSON shape so main-016 can extend it without
      breaking v1 reads.
- **Out of scope (do not creep):**
  - **UI** to mutate `DefaultVoiceId` — that's main-016. v1 ships only the
    storage layer; the picker reads it but offers no "make this the default"
    affordance.
  - Speak request history / replay UI.
  - Editing voice metadata from this page.
  - Audio editing / trimming on the saved `.wav`.
  - Priority lanes / barge-to-front (deferred per ADR 0007 to v1.5).
- **Status:** Promoted to `todo/` on 2026-05-04. All dependencies
  (main-010 styleguide, main-011 pocket-tts bootstrap, main-020 navigation
  shell) are in `done/`; ready for a worker to pick up.

## Outcome

Speak page shipped per spec. Three new singletons:
`Services\Speak\VoiceCatalog.cs` (the `/voices` + picker single source of
truth — Q4), `Services\Speak\SpeakService.cs` (request construction +
status state-machine + off-queue `RenderToFileAsync` for Save — Q2 / Q5 /
Q6), and `Services\Settings\UserSettings.cs` (typed wrapper over
`%LOCALAPPDATA%\Mockingbird\settings.json` with `DefaultVoiceId` only in v1,
forward-compatible JSON shape — Q7). `SpeakQueue` gained two events
(`RequestStarted` / `RequestCompleted`) which `SpeakService` listens to
for the four-label state machine (idle / synthesising / playing / stopped).
`SpeakServer.MapPost("/speak", …)` now routes through `SpeakService.Enqueue`,
`MapPost("/stop", …)` through `SpeakService.StopAndDrain`, and
`MapGet("/voices", …)` through `VoiceCatalog.ListAsync` — same call sites
as the page. The Speak page (`Views\Pages\SpeakPage.xaml`+`.cs`) is a
four-row Grid with the dominant `ui:TextBox`, voice `ComboBox`, Play / Stop /
Save button row (Save shows an inline `ui:ProgressRing` via the
`[RelayCommand]`-generated `IsRunning`), and a status `TextBlock`.
`SpeakPageViewModel` uses `[ObservableProperty]` /
`[NotifyCanExecuteChangedFor]` / `[RelayCommand]` per ADR 0010. DI
registrations added in `EntryPoint.cs`. Build clean: `dotnet build
mockingbird.sln -c Debug` → 0 errors, 0 warnings. Interactive UI
behaviours (textbox dominates, dialog opens, status transitions visibly)
were assume-passed — code is in place per spec.

Key files:
- `src\Mockingbird\Services\Speak\VoiceCatalog.cs` (new)
- `src\Mockingbird\Services\Speak\SpeakService.cs` (new)
- `src\Mockingbird\Services\Settings\UserSettings.cs` (new)
- `src\Mockingbird\Services\Speak\SpeakQueue.cs` (added two events)
- `src\Mockingbird\Services\Http\SpeakServer.cs` (refactored to use the new seams)
- `src\Mockingbird\Views\Pages\SpeakPage.xaml`+`.cs` (real Speak surface)
- `src\Mockingbird\ViewModels\Pages\SpeakPageViewModel.cs` (real VM)
- `src\Mockingbird\EntryPoint.cs` (DI registrations)
