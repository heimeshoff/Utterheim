# Context: main

## Purpose

The single bounded context for Mockingbird. Owns everything from voice acquisition through synthesis to delivery: voice profile management, sample capture, speak-request queueing, streaming TTS playback, the Claude Code integration surface, and the tray UI that wraps it all.

This is a personal tool with one user and one primary consumer (Claude Code). The whole app is one coherent subject; see `../../context-map.md` for why the candidate sub-boundaries (synthesis, voice-library, voice-capture, claude-bridge, tray-ui) were rejected as separate BCs.

## Classification

**Mixed — primarily core, with supporting and generic regions inside it.**

- **Core**: the *voice diversity* loop — letting the user clone arbitrary voices from any audio source they have, persist them as `.safetensors` profiles, and route them per Claude session. This is the differentiator. No off-the-shelf product does exactly this for this user's workflow.
- **Supporting**: the speak queue, stop semantics, output device selection, tray UI shell. Real work, but not the differentiator — these exist to make the core loop usable.
- **Generic**: audio capture plumbing (WASAPI loopback, microphone), global hotkeys, settings persistence, tray icon, the synthesis engine itself (pocket-tts is consumed, not built). These are reused from WhisperHeim or vendored from libraries.

The fact that the BC straddles all three classifications is a normal property of small personal tools — there isn't enough domain mass to justify cutting along the classification line.

## Core language

From the vision's seed glossary, plus terms that surfaced during boundary analysis:

| Term | Meaning |
|---|---|
| **Voice profile** | A named, persistent representation of a voice that pocket-tts can reuse instantly. `.safetensors` file plus metadata. The unit of voice identity. |
| **Sample clip** | The 5–20-second audio snippet used to create a voice profile. Source: microphone, WASAPI loopback, or imported file. |
| **Built-in voice** | A voice profile shipped with pocket-tts (alba, marius, javert, jean, fantine, cosette, eponine, azelma). |
| **Cloned voice** | A user-created voice profile. |
| **Speak request** | An incoming call carrying `{text, voice ID}`. The unit of work the BC queues, synthesizes, and plays. |
| **Speak queue** | FIFO of pending speak requests. Head plays; tail is appended. Advances on completion or stop. |
| **Stop signal** | A user action (double-tap LCtrl) that halts current playback. Drain-vs-keep semantics is an open question (see vision). |
| **Loopback capture** | Recording the system output device via WASAPI loopback. The "record what I'm hearing" source for voice cloning. |
| **First-chunk latency** | Time from speak request to first audio sample at the speakers. Target ≤2 s; measured ~190 ms warm / ~320 ms cold (median, alba) and input-length-independent after main-024 / ADR 0013. |
| **Streaming synthesis** | Producing audio in chunks during generation so playback starts before the full utterance is rendered. |
| **Capture session** | An interactive recording episode that produces (or rejects) a single sample clip. |
| **Engine** | The TTS implementation behind a profile. v1 has one engine (pocket-tts). A profile records its engine so future multi-engine selection is possible. |
| **Speak endpoint** | The localhost-only HTTP/IPC surface Claude Code calls to enqueue speak requests. The published interface of this BC. |

## Key actors

- **The user** (single developer) — clones voices, manages the library, configures settings, hits the stop hotkey, watches the tray status. Episodic interaction.
- **Claude Code sessions** (multiple, parallel) — the primary speak-request producers. They call the speak endpoint and expect audio to come out within ~2 s. They do not know about the queue, the voice library, or the engine; they only know `{text, voice}`.
- **pocket-tts** (external engine, consumed) — the conformist dependency. Mockingbird adapts to its API.

## Relationships

This BC is the only domain BC. External relationships:

- **Upstream conformist** to **pocket-tts**: mockingbird adapts to whatever shape pocket-tts exposes (`TTSModel`, voice states, Mimi encoding, `.safetensors`). Wrapped behind a thin internal engine interface to leave room for a second engine later.
- **Open host (published language)** to **Claude Code**: the speak endpoint is a stable, documented localhost surface that any Claude hook can call. The wire format (`{text, voice}` plus stop / status) is the published language.
- **Library consumer** of **WhisperHeim shared services**: audio capture, hotkeys, settings, startup. Not a BC relationship — these are technical libraries with no domain language.

## UI / styleguide gate

This BC has substantial frontend surface (tray icon + tray window with voice library, capture flow, voice-test playback, settings — modeled on WhisperHeim's design.md). 

**Rule:** every frontend-bearing task in this BC `depends_on` the styleguide task **`main-010`** (`done/main-010-styleguide.md`, artefact at [`docs/styleguide.md`](../../../docs/styleguide.md)). The gate is **OPEN** as of 2026-05-01 — Marco Heimeshoff signed off on the styleguide. Frontend feature tasks may now be promoted from `backlog/` to `todo/`. This protects the "feels like a first-party Windows app — Mica backdrop, Fluent controls, Segoe UI Variable, the WhisperHeim aesthetic" success criterion in the vision.

Note: the walking skeleton (`main-009`) was always exempt — its UI is a placeholder shell whose purpose is foundation, not presentation.

## Code structure

The walking skeleton (main-009) materialises ADRs 0001–0008 as code. main-020
adds the navigation shell on top per ADRs 0009 and 0010. Top-level layout:

```
mockingbird.sln
src\
  Mockingbird\                        WPF tray app, net9.0-windows x64
    EntryPoint.cs                     composition root + IHost lifecycle
    App.xaml(.cs)                     WPF Application + WPF-UI theme dictionary
    Views\
      MainWindow.xaml(.cs)            Mica FluentWindow + ui:NavigationView shell
                                      (sidebar + four-page set + status footer) +
                                      tray:NotifyIcon menu
      BootstrapDialog.xaml(.cs)       first-run dialog (placeholder in v1)
      Pages\
        SpeakPage.xaml(.cs)           Stub in main-020 — main-013 fills it
        VoicesPage.xaml(.cs)          Voice library list with per-row preview (main-014)
        SettingsPage.xaml(.cs)        Stub in main-020 — main-016 fills it
        AboutPage.xaml(.cs)           Stub in main-020 — main-017 fills it
    ViewModels\
      EngineStatusViewModel.cs        Backs the persistent footer (HTTP +
                                      Engine state, live via SidecarHost.StateChanged)
      Pages\
        SpeakPageViewModel.cs         Speak page VM — Text / Voices / SelectedVoice /
                                      StatusLabel + Play / Stop / Save commands (main-013)
        VoicesPageViewModel.cs        Voices page VM (main-014) — BuiltInVoices /
                                      ClonedVoices / per-row PreviewCommand routed
                                      through SpeakService.Enqueue (ADR 0014); plus a
                                      VoiceRowViewModel inner class for each list row
        SettingsPageViewModel.cs      Empty ObservableObject stub (main-016 fills)
        AboutPageViewModel.cs         Empty ObservableObject stub (main-017 fills)
    Services\
      Navigation\PageService.cs       Thin IPageService → IServiceProvider adapter (ADR 0009)
      Tts\
        ITtsEngine.cs                 the seam every TTS engine plugs into
        StubTtsEngine.cs              440 Hz test tone (replaced by main-011)
        SidecarHost.cs                Owns python sidecar lifecycle; raises
                                      StateChanged for the footer VM
        ProcessJobObject.cs           Win32 Job Object wrapper (ADR 0012) —
                                      KILL_ON_JOB_CLOSE keeps the python tree
                                      from outliving the host (main-022)
      Speak\
        SpeakRequest.cs               unit of work
        SpeakQueue.cs                 Channel<T> worker (ADR 0007, 0004) +
                                      RequestStarted / RequestCompleted events (main-013)
        AudioPlayer.cs                NAudio WaveOutEvent wrapper
        VoiceCatalog.cs               Single source of truth for the voice list —
                                      shared by HTTP /voices and the Speak page
                                      picker (main-013, Q4)
        SpeakService.cs               In-process seam shared by HTTP /speak and the
                                      Speak page Play button. Owns request construction,
                                      surfaces the four-label status state-machine, and
                                      provides off-queue RenderToFileAsync for Save
                                      (main-013, Q2 / Q5 / Q6)
      Http\SpeakServer.cs             Kestrel minimal API on 127.0.0.1:7223 (ADR 0003).
                                      /speak and /voices route through SpeakService /
                                      VoiceCatalog (main-013).
      Hotkey\
        NativeMethods.cs              copied from WhisperHeim @ 911bff0
        DoubleTapDetector.cs          mockingbird-specific LCtrl gesture (ADR 0006)
      Settings\
        DataPathService.cs            ADR 0005 path layout (adapted from WhisperHeim)
        UserSettings.cs               Typed wrapper over %LOCALAPPDATA%\Mockingbird\
                                      settings.json — v1 stores DefaultVoiceId only;
                                      forward-compatible JSON shape for main-016 (main-013)
    appsettings.json                  default port + hotkey window
  Mockingbird.Cli\                    mockingbird-speak — single-file CLI wrapper
assets\branding\                      brand mark + raster outputs
  mockingbird-logo.svg                source artwork (placeholder, signed off)
  mockingbird-logo-{16..512}.png      rasterised PNGs (main-012)
  mockingbird.ico                     multi-resolution tray + taskbar icon
Tools\RasteriseLogo\                  one-shot helper that produces the PNG/ICO
                                      from the source SVG. Outside mockingbird.sln.
README.md
```

Path layout at runtime (per ADR 0005):

```
%APPDATA%\Mockingbird\bootstrap.json    machine-local data-path pointer
%LOCALAPPDATA%\Mockingbird\
  logs\mockingbird-YYYYMMDD.log         Serilog rolling sink (ADR 0008)
  runtime\python\                       (main-011 will populate this)
  models\pocket-tts\                    (main-011 will populate this)
  cache\
  bootstrap-state.json                  first-run completion marker
  settings.json                         UserSettings — DefaultVoiceId in v1 (main-013)
<dataPath>\voices\library.json          empty list in v1 skeleton
```

## UI shell

As of main-020 the tray window hosts a **wpfui `NavigationView`** with the
canonical four-page set (Speak / Voices / Settings / About) per ADR 0009.
Pages are real WPF `Page` subclasses, registered transient in DI, and
implement both `Wpf.Ui.Controls.INavigableView<TViewModel>` (typed VM
accessor) and `Wpf.Ui.Controls.INavigationAware` (`OnNavigatedTo` /
`OnNavigatedFrom` lifecycle hooks). A thin `PageService` delegates wpfui's
`IPageService` calls to the host's `IServiceProvider`. View-models use the
`CommunityToolkit.Mvvm` source-generator attributes (`[ObservableProperty]`,
`[RelayCommand]`) per ADR 0010 — no hand-rolled `ObservableBase` /
`RelayCommand` helpers.

A persistent **status footer** below the navigation area shows
`HTTP {host}:{port}  •  Engine: {state}`. Backed by
`EngineStatusViewModel`; the engine label updates live as
`SidecarHost.StateChanged` fires (`starting` → `running` → `restarting` →
`failed` → `stopping`). When the stub engine is selected
(`MOCKINGBIRD_USE_STUB_ENGINE=1`) the label reads `stub` and never changes.
The four feature pages (main-013/014/016/017) replace each stub with real
content; main-017 may surface a richer status panel and remove the footer
at that point.

## Engine status

As of main-011 the **real pocket-tts engine is wired in**:

- `PocketTtsEngine` posts text to the Python sidecar's `POST /tts` form endpoint and
  streams 24 kHz mono 16-bit PCM chunks (after stripping the WAV header) straight to
  `AudioPlayer` via the existing `IAsyncEnumerable<byte[]>` path. The HTTP call uses
  `HttpCompletionOption.ResponseHeadersRead` per ADR 0013 — the default
  `ResponseContentRead` would buffer the entire WAV, scaling first-chunk latency
  linearly with input size and breaking the ≤2 s budget on anything beyond a short
  sentence.
- `SidecarHost` owns the `python.exe -m pocket_tts serve --host 127.0.0.1 --port 0`
  process: parses the assigned port from Uvicorn's startup banner, polls `/health`,
  redirects stdout/stderr into Serilog under a `sidecar` source, terminates on host
  shutdown, and restarts on crash with capped exponential backoff (5 attempts).
  Per ADR 0012, every spawned python is bound to a Win32 Job Object with
  `KILL_ON_JOB_CLOSE`, so the entire process tree (uvicorn workers, multiprocessing
  spawn, etc.) dies atomically on tray Exit — *and* on abrupt host death — with no
  zombie `python.exe` left in Task Manager. A `_shuttingDown` flag suppresses the
  auto-restart loop during host shutdown so the supervisor cannot respawn the
  python we just killed.
- `PythonRuntimeBootstrapper` runs once on first launch: downloads Python 3.12.7
  embeddable to `%LOCALAPPDATA%\Mockingbird\runtime\python\`, enables `site` in the
  `._pth` file, bootstraps pip, pip-installs `pocket-tts>=2.0,<3` (which pulls torch
  CPU plus deps, ~600 MB), and smoke-tests the import. Progress is persisted to
  `bootstrap-state.json` so a half-finished run resumes on restart. Per ADR 0011,
  on-disk sentinel files (`python.exe`, `pip`, `pocket_tts/__init__.py`) are
  authoritative — a stale state file cannot trick the bootstrapper into skipping a
  step whose artefacts have been wiped — and any subprocess that exits non-zero
  surfaces its captured stderr tail in both the file log (at `Error`) and the
  thrown exception (visible in the `BootstrapDialog`).
- `BootstrapDialog` drives the bootstrapper with per-step progress, cancel, and retry.
- `StubTtsEngine` is preserved behind `MOCKINGBIRD_USE_STUB_ENGINE=1` for offline /
  CI testing; the env flag also disables the sidecar and bootstrap-dialog wiring.
- `GET /voices` returns the eight pocket-tts built-ins (`alba`, `marius`, `javert`,
  `jean`, `fantine`, `cosette`, `eponine`, `azelma`) with `engine: "pocket-tts"`,
  `isBuiltIn: true`. Voice cloning UI and the wider voice library are separate tasks.
- `GET /status` reports `sidecar.state` (notstarted / starting / running / restarting
  / failed / stopping), `sidecar.healthy`, `sidecar.port`, and `sidecar.lastError`.

The "stub-engine plays a 440 Hz tone" note from the skeleton is now superseded.

## Speak page

As of main-013 the Speak page replaces the main-020 stub with the real daily-use
surface — the page mirrors WhisperHeim's TTS section A:

- A four-row Grid with a 16 px outer margin: dominant multi-line `ui:TextBox`
  (`AcceptsReturn`, `MinHeight=200`, internal scrollbar), a left-aligned
  `ComboBox` voice picker, a horizontal Play / Stop / Save button row, and a
  status `TextBlock` under it.
- View-model `SpeakPageViewModel` (`CommunityToolkit.Mvvm`,
  `[ObservableProperty]` + `[RelayCommand]` per ADR 0010). `Play`'s and
  `Save`'s `CanExecute` reactivities come from
  `[NotifyCanExecuteChangedFor]` on `Text` and `SelectedVoice`. Save uses
  `[RelayCommand]` async, which yields `IsRunning` for the inline
  `ui:ProgressRing`.
- The page implements both `INavigableView<SpeakPageViewModel>` (typed VM) and
  `INavigationAware` (`OnNavigatedTo` / `OnNavigatedFrom`); on navigated-to it
  refreshes voices, applies the latest `SpeakStatus`, and subscribes to
  `SpeakService.StatusChanged` + `VoiceCatalog.VoicesChanged`. On
  navigated-from it unsubscribes and cancels in-flight refreshes.

### In-process seams (Q2 + Q4)

Both the HTTP API and the page are routed through the same singletons so
adding cloned voices (main-015) or augmenting request construction lands in
both surfaces in one place:

- `VoiceCatalog.ListAsync(ct)` is the single source of truth for the voice
  list. v1 delegates to `ITtsEngine.ListVoicesAsync` and fires
  `VoicesChanged` once on first population. main-015 will fold cloned voices
  into the same return.
- `SpeakService.Enqueue(text, voiceId)` constructs the `SpeakRequest`
  (Guid, voice-id fallback, validation) and calls `SpeakQueue.Enqueue`.
  `SpeakService.StopAndDrain()` is a thin pass-through to the queue.
  `SpeakService.RenderToFileAsync(...)` is the off-queue Save path: it
  streams `ITtsEngine` output directly into a `NAudio.Wave.WaveFileWriter`
  and never touches `SpeakQueue` or `AudioPlayer` — Save can never interrupt
  a live playback request.
- `SpeakServer.MapPost("/speak", ...)` calls `SpeakService.Enqueue`,
  `MapPost("/stop", ...)` calls `SpeakService.StopAndDrain`,
  `MapGet("/voices", ...)` calls `VoiceCatalog.ListAsync`.

### Status state-machine (Q6)

`SpeakService` exposes a `StatusChanged` event carrying a `SpeakStatus`
record `(Kind, VoiceId)` with four `Kind` values:

- `Idle` — no `_current` request, queue empty.
- `Synthesising` — `RequestStarted` fired, no PCM has hit the device yet.
- `Playing` — `AudioPlayer.IsPlaying` flipped to true (poll loop, 100 ms).
- `Stopped` — transient label after `StopAndDrain`; auto-clears to `Idle`
  after 2000 ms via a Task.Delay timer (cancelled / replaced if a new
  Stop arrives in the window).

The label format is `{kind}` for `idle` / `stopped`, `{kind} — {voiceId}`
for `synthesising` / `playing`. `synthesising` may be invisibly brief
because pocket-tts streams chunks with ~200 ms first-chunk latency — the
poll catches `Playing` quickly. `RequestStarted` and `RequestCompleted`
were added on `SpeakQueue` to drive the machine.

### UserSettings (Q7)

The voice picker pre-selects `UserSettings.DefaultVoiceId` if it is set and
present in the catalog; otherwise it falls back to the alphabetically-first
voice (`alba` for the eight pocket-tts built-ins). `UserSettings` reads /
writes `%LOCALAPPDATA%\Mockingbird\settings.json` with an atomic
temp+replace; unknown JSON fields are ignored so main-016 can extend the
schema without breaking v1 reads. v1 ships only the storage layer — UI to
mutate the default voice is main-016's responsibility. The HTTP `/speak`
endpoint is unaffected (callers always specify a voice explicitly).

### Verification note

The build is clean (`dotnet build mockingbird.sln -c Debug` → 0 errors,
0 warnings). The interactive UI behaviours — textbox dominates the layout,
SaveFileDialog opens with the correct filter, Stop drains the queue,
status line transitions visibly, picker refreshes on navigation — are
**not interactively re-tested** in this pass; the code is in place per
the main-013 spec and any regression will surface during the next
manual run.

## Voices page

As of main-014 the Voices page replaces the main-020 stub with the real voice
library surface — a read-only list shell that main-015 will fold cloned
voices into without touching this page's code:

- A two-row Grid with a 16 px outer margin: a header (`FontWeight="Light"` page
  title plus a small `{TotalCount} voices` sub-label) and a scrollable list
  underneath. Inside the `ScrollViewer`, two grouped sections — **Built-in**
  first, **Cloned** second — both with `SemiBold` section headers. Each row
  is a three-column Grid: name + meta (engine), Preview button (`Play24`
  icon, `Appearance="Secondary"`), and a per-row active-request indicator
  (`Speaker224` symbol, hidden when idle).
- View-model `VoicesPageViewModel` (`CommunityToolkit.Mvvm`,
  `[ObservableProperty]` per ADR 0010) exposes two
  `ObservableCollection<VoiceRowViewModel>`s (`BuiltInVoices`, `ClonedVoices`),
  populated by partitioning `VoiceCatalog.ListAsync()`'s result on
  `IsBuiltIn`. The page **does not read `library.json`** — main-015 owns
  that layer end-to-end and feeds the catalog. `ClonedSectionIsEmpty`
  drives the inline empty-state message (`"No cloned voices yet — see
  main-015 to clone your own."`) that ships in v1.
- The page implements both `INavigableView<VoicesPageViewModel>` and
  `INavigationAware`. On navigated-to it seeds the engine state from
  `SidecarHost.GetStatus()`, applies the current `SpeakStatus`, refreshes
  voices, and subscribes to `VoiceCatalog.VoicesChanged`,
  `SpeakService.StatusChanged`, and `SidecarHost.StateChanged`. On
  navigated-from it unsubscribes and cancels in-flight refreshes.

### Preview routes through the speak queue (ADR 0014)

Per-row Preview calls `SpeakService.Enqueue($"Hello, this is {DisplayName}.",
voiceId)` — the same in-process seam main-013 wired up for the Speak page's
Play button and that `POST /speak` already uses. **Not** a direct
`ITtsEngine.StreamAsync`, **not** a side `AudioPlayer`. ADR 0014 governs;
the rationale is the queue's single-arbiter invariant: a direct off-queue
path would race Claude-driven utterances for the output device and break
the stop-hotkey contract (ADR 0004).

Behavioural consequences inherited for free:

- Click Preview while another preview / Claude utterance is in flight →
  enqueues behind it (FIFO per ADR 0007, no barge in v1).
- Double-tap LCtrl during a preview → drains the queue per ADR 0004,
  identical to tray Stop and `POST /stop`. The row's active-request
  indicator flips off; status footer briefly shows `stopped` then `idle`.
- First-chunk latency = full queue latency (~190 ms warm for the canned
  phrase, per ADR 0013). Lanes for preview-bargeing are deferred to v1.5.

### Engine-state visibility

The page binds three coarse view-states to `SidecarHost.State`:

- `starting` / `restarting` / `notstarted` → centred `ui:ProgressRing` +
  "Voice engine is starting..." in place of the list. Per-row Preview
  buttons would be disabled anyway (no rows).
- `failed` → an inline error banner using `SystemFillColorCriticalBackgroundBrush`:
  "Voice engine failed to start. See the About page for details and retry."
  (main-017 will surface the richer panel and a retry control.)
- `running` → the normal two-section list. Preview buttons enabled per row.

`VoicesPageViewModel.ApplyEngineState(...)` flips both the placeholder
visibility flags (`IsLoading` / `IsFailed` / `IsRunning`) and each row's
`CanPreview` (which guards the Preview command) in one shot.

### Per-row active-request indicator

A page-VM-level subscription to `SpeakService.StatusChanged` finds the row
whose `VoiceId` matches the active request and toggles its `IsActiveRequest`
flag. When status flips to `Idle` / `Stopped`, all rows return to inactive.
The dispatcher hop matches the pattern in `EngineStatusViewModel`.

### Refresh behaviour

`OnNavigatedTo` and `VoiceCatalog.VoicesChanged` both call
`RefreshVoicesAsync` (re-entrancy-guarded so a `VoicesChanged` event
arriving mid-`OnNavigatedTo` doesn't double-refresh). When the engine
transitions into `running` from a starting / restarting state mid-page-life,
the code-behind also forces a refresh so the list populates without
re-navigation. **No `library.json` file watcher** — the catalog is the
single source of truth (main-015 will fire `VoicesChanged` on save / delete).

### Out-of-scope reminders for v1

- No delete affordance — moved to main-015 where cloned voices the button
  would operate on actually exist.
- No search / filter / tags — vision-deferred until ~15 voices.
- No per-session voice routing UI — env-var-only per main-019.
- No per-row preview error toasts — footer + log file is sufficient signal.

## Claude Code integration kit

As of main-019, the bridge from "the speak endpoint exists" to "Claude
Code sessions actually talk to me" ships as documentation + a sample
script under `examples/claude-hooks/`:

- `mockingbird-hook.ps1` — small PowerShell shim that POSTs `{text, voice}`
  to `http://127.0.0.1:7223/speak`. Reads voice from
  `$env:MOCKINGBIRD_VOICE` (default `alba`) and endpoint from
  `$env:MOCKINGBIRD_ENDPOINT` (default per ADR 0003). `-Silent` swallows
  all failures so a missing sidecar can never block Claude Code itself.
- `README.md` — wiring recipe for Claude Code's `Stop` and `Notification`
  hooks, voice-assignment convention (env var per terminal), the parallel
  two-session worked example that demonstrates the v1 payoff, and a
  troubleshooting section covering the failure modes surfaced by
  main-018 verification (sidecar not running, port collision, cold-start
  latency on long input, watchdog vs `/status`-polling races).

Voice routing is **caller-side by design** — the BC has no concept of a
session id, and per-session voice is purely an env-var contract between
the user's shell and the hook script. Server-mediated routing would be
a separate decision task.

## Notes for the architect

ADRs 0001–0008 are committed. The walking skeleton (main-009) materialised them as
code; main-011 replaced the stub engine with the real pocket-tts sidecar (see "Engine
status" above). Every architectural seam — HTTP, queue, NAudio playback, hotkey,
tray, logging, path layout, CLI wrapper, sidecar lifecycle, runtime bootstrap — is
real and end-to-end verified.

Historical: the boundary analysis surfaced four architectural decisions which
have now been resolved by the ADRs above:

1. Python-vs-C# integration shape for pocket-tts → ADR 0002 (Python sidecar).
2. Claude-to-mockingbird transport → ADR 0003 (HTTP loopback on :7223).
3. Stop-signal semantics → ADR 0004 (drain queue by default).
4. WhisperHeim reuse form → ADR 0006 (copy-and-modify in v1).
