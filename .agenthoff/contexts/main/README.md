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
| **Voice library** | The on-disk catalog of cloned voices: `<dataPath>\voices\library.json` (master index) plus `<dataPath>\voices\<id>\` (per-voice profile + meta + optional sample). Persistence layer for "own-your-voices". |
| **Voice id** | Stable lowercase-kebab folder name (`marco`, `marco-a3f2`). Generated from display name; never renamed. Collisions with built-in voice ids are rejected; collisions among cloned voices get a 4-hex disambiguator. |
| **Library reconciliation** | Startup pass that brings `library.json` and on-disk voice folders into agreement: prune missing-on-disk entries, reinsert orphan folders from `meta.json`, log warnings for unreadable / future-schema files. |
| **Sidecar wrapper** | The `mockingbird_sidecar` Python module that mounts mockingbird-owned routes (`/export-voice`, `/tts-with-state`) on top of pocket-tts's FastAPI app, sharing its resident `tts_model`. Per ADR 0015. |

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
                                      tray:NotifyIcon menu + RootContentDialogPresenter
                                      that hosts in-window ContentDialogs (main-026)
      BootstrapDialog.xaml(.cs)       first-run dialog (placeholder in v1)
      Pages\
        SpeakPage.xaml(.cs)           Stub in main-020 — main-013 fills it
        VoicesPage.xaml(.cs)          Voice library list with per-row preview (main-014)
                                      and per-row Delete on cloned rows (main-026)
        SettingsPage.xaml(.cs)        Stub in main-020 — main-016 fills it
        AboutPage.xaml(.cs)           Stub in main-020 — main-017 fills it
      Dialogs\
        DeleteVoiceDialog.xaml(.cs)   Fluent ContentDialog for the per-row Delete
                                      affordance on cloned voices (main-026).
    ViewModels\
      EngineStatusViewModel.cs        Backs the persistent footer (HTTP +
                                      Engine state, live via SidecarHost.StateChanged)
      Dialogs\
        DeleteVoiceDialogViewModel.cs Backs DeleteVoiceDialog (main-026) — voice id
                                      + display name, IsDeleting / ErrorMessage flags,
                                      Delete / Cancel commands; on success hides itself,
                                      on IO failure surfaces an inline error and stays open.
      Pages\
        SpeakPageViewModel.cs         Speak page VM — Text / Voices / SelectedVoice /
                                      StatusLabel + Play / Stop / Save commands (main-013)
        VoicesPageViewModel.cs        Voices page VM (main-014) — BuiltInVoices /
                                      ClonedVoices / per-row PreviewCommand routed
                                      through SpeakService.Enqueue (ADR 0014); plus a
                                      VoiceRowViewModel inner class for each list row.
                                      Composes a VoiceCloningViewModel for the cloning
                                      sub-panel (main-025).
        VoiceCloningViewModel.cs      Cloning sub-VM (main-025) — drives the source
                                      toggle (Microphone | System Audio), device
                                      selectors, level meter, duration / progress,
                                      voice-name validation, and the Save flow
                                      (render WAV → POST /export-voice →
                                      VoiceLibraryService.AddAsync). Minimum 5 s,
                                      soft cap 30 s, hard cap 60 s auto-stop.
        VoicesPageConverters.cs       NullOrEmptyToVisibilityConverter +
                                      CloningSourceToBoolConverter for the cloning
                                      panel (main-025).
        SettingsPageViewModel.cs      Empty ObservableObject stub (main-016 fills)
        AboutPageViewModel.cs         Empty ObservableObject stub (main-017 fills)
    Services\
      Navigation\PageService.cs       Thin IPageService → IServiceProvider adapter (ADR 0009)
      Tts\
        ITtsEngine.cs                 the seam every TTS engine plugs into
        StubTtsEngine.cs              440 Hz test tone (replaced by main-011)
        SidecarHost.cs                Owns python sidecar lifecycle; raises
                                      StateChanged for the footer VM. Spawns
                                      `python -m mockingbird_sidecar serve`
                                      (per ADR 0015) — same uvicorn banner so
                                      port discovery is unchanged from pocket_tts.
        PocketTtsEngine.cs            Real engine: built-in voices route /tts
                                      with voice_url; cloned voices resolve to
                                      profile.safetensors via VoiceLibraryService
                                      and route /tts-with-state (ADR 0015 / main-015).
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
                                      picker (main-013, Q4). Composes engine
                                      built-ins ∪ VoiceLibraryService.ListClonedAsync
                                      (main-015) and re-fires VoicesChanged on
                                      every LibraryChanged.
        SpeakService.cs               In-process seam shared by HTTP /speak and the
                                      Speak page Play button. Owns request construction,
                                      surfaces the four-label status state-machine, and
                                      provides off-queue RenderToFileAsync for Save
                                      (main-013, Q2 / Q5 / Q6)
      Voices\
        ClonedVoiceMeta.cs            Schema records (ClonedVoiceMeta v1,
                                      ClonedVoiceIndexEntry, VoiceLibraryFile,
                                      VoiceSource enum, LibraryChangedArgs,
                                      VoiceValidationException) per ADR 0005 +
                                      main-015 schema ratification.
        VoiceLibraryService.cs        Owns <dataPath>\voices\* — temp+rename for every
                                      mutation (profile.safetensors → meta.json →
                                      library.json), id sanitisation + built-in
                                      collision rejection, startup reconciliation
                                      between library.json and on-disk folders,
                                      LibraryChanged event for the catalog.
        VoiceLibraryStartup.cs        Hosted-service shim: runs LoadAsync once on
                                      host start so the catalog has cloned rows
                                      before page VMs resolve.
        VoiceCloningClient.cs         HTTP client wrapping the sidecar's POST
                                      /export-voice endpoint (ADR 0015) — uploads
                                      a sample WAV, receives .safetensors bytes
                                      that the C# host persists.
      Http\SpeakServer.cs             Kestrel minimal API on 127.0.0.1:7223 (ADR 0003).
                                      /speak and /voices route through SpeakService /
                                      VoiceCatalog (main-013).
      Audio\                            Mic + WASAPI loopback capture for voice
                                        cloning (main-025). Adapted from
                                        WhisperHeim @ 911bff0 per ADR 0006.
        IAudioCaptureService.cs       Mic capture interface (16 kHz mono 16-bit).
        AudioCaptureService.cs        NAudio WaveInEvent implementation.
        IHighQualityLoopbackService.cs WASAPI loopback interface (native format,
                                        no resampling — pocket-tts handles it).
                                        SaveAsVoice removed — persistence routes
                                        through VoiceLibraryService per ADR 0005.
        HighQualityLoopbackService.cs WasapiLoopbackCapture impl writing the
                                        captured audio to a temp WAV under
                                        %TEMP%\Mockingbird\.
        AudioDeviceInfo.cs            Shared device-info record.
        AudioDeviceResolver.cs        WaveIn enumeration + Core Audio name resolution.
        AudioRingBuffer.cs            Thread-safe lock-free ring buffer used by
                                        AudioCaptureService.
      Hotkey\
        NativeMethods.cs              copied from WhisperHeim @ 911bff0
        DoubleTapDetector.cs          mockingbird-specific LCtrl gesture (ADR 0006)
      Settings\
        DataPathService.cs            ADR 0005 path layout (adapted from WhisperHeim)
        UserSettings.cs               Typed wrapper over %LOCALAPPDATA%\Mockingbird\
                                      settings.json — v1 stores DefaultVoiceId only;
                                      forward-compatible JSON shape for main-016 (main-013)
    appsettings.json                  default port + hotkey window
    PythonSidecar\
      mockingbird_sidecar\            mockingbird-owned Python wrapper around pocket_tts
                                      (ADR 0015 / main-015). Bundled next to the .exe;
                                      bootstrapper copies into runtime\python\Lib\
                                      site-packages\mockingbird_sidecar\ on first launch.
                                      Adds /export-voice and /tts-with-state to the
                                      pocket_tts FastAPI app while keeping its TTSModel
                                      resident across requests.
        __init__.py                   package marker
        __main__.py                   `python -m mockingbird_sidecar` entry point
        main.py                       FastAPI route definitions + typer `serve` command
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
  runtime\python\                       embeddable Python + pocket_tts +
                                        mockingbird_sidecar (main-011 / main-015)
  models\pocket-tts\                    (main-011 will populate this)
  cache\
  bootstrap-state.json                  first-run completion marker
  settings.json                         UserSettings — DefaultVoiceId in v1 (main-013)
<dataPath>\voices\library.json          { schemaVersion: 1, voices: [...] }
                                        — id/name/engine/source/createdAt per row
                                        (main-015)
<dataPath>\voices\<id>\                 per cloned voice (main-015)
  profile.safetensors                   exported voice state from /export-voice
  meta.json                             schemaVersion 1 + sampleSeconds + tags
  sample.wav                            optional retained sample
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

- `PocketTtsEngine` posts text to the Python sidecar and streams 24 kHz mono 16-bit
  PCM chunks (after stripping the WAV header) straight to `AudioPlayer` via the
  existing `IAsyncEnumerable<byte[]>` path. **Built-in voices** route to `POST /tts`
  with form-encoded `voice_url`; **cloned voices** (main-015) resolve to
  `<dataPath>\voices\<id>\profile.safetensors` via `VoiceLibraryService` and route
  to `POST /tts-with-state` with the profile uploaded as `voice_state`. Both calls
  use `HttpCompletionOption.ResponseHeadersRead` per ADR 0013 — the default
  `ResponseContentRead` would buffer the entire WAV, scaling first-chunk latency
  linearly with input size and breaking the ≤2 s budget on anything beyond a short
  sentence.
- `SidecarHost` owns the `python.exe -m mockingbird_sidecar serve --host 127.0.0.1
  --port 0` process (per ADR 0015 — the wrapper module imports pocket_tts's FastAPI
  app and mounts `/export-voice` + `/tts-with-state` so cloning + cloned-voice
  synthesis share the same resident `tts_model`): parses the assigned port from
  Uvicorn's startup banner (unchanged regex — uvicorn logs the same line), polls
  `/health`, redirects stdout/stderr into Serilog under a `sidecar` source, terminates
  on host shutdown, and restarts on crash with capped exponential backoff (5 attempts).
  Per ADR 0012, every spawned python is bound to a Win32 Job Object with
  `KILL_ON_JOB_CLOSE`, so the entire process tree (uvicorn workers, multiprocessing
  spawn, etc.) dies atomically on tray Exit — *and* on abrupt host death — with no
  zombie `python.exe` left in Task Manager. A `_shuttingDown` flag suppresses the
  auto-restart loop during host shutdown so the supervisor cannot respawn the
  python we just killed.
- `PythonRuntimeBootstrapper` runs once on first launch: downloads Python 3.12.7
  embeddable to `%LOCALAPPDATA%\Mockingbird\runtime\python\`, enables `site` in the
  `._pth` file, bootstraps pip, pip-installs `pocket-tts>=2.0,<3` (which pulls torch
  CPU plus deps, ~600 MB), copies the bundled `mockingbird_sidecar` wrapper from the
  install folder into `runtime\python\Lib\site-packages\mockingbird_sidecar\`
  (main-015), and smoke-tests both imports. Progress is persisted to
  `bootstrap-state.json` so a half-finished run resumes on restart. Per ADR 0011,
  on-disk sentinel files (`python.exe`, `pip`, `pocket_tts/__init__.py`,
  `mockingbird_sidecar/__init__.py` + `mockingbird_sidecar/main.py`) are
  authoritative — a stale state file cannot trick the bootstrapper into skipping a
  step whose artefacts have been wiped — and any subprocess that exits non-zero
  surfaces its captured stderr tail in both the file log (at `Error`) and the
  thrown exception (visible in the `BootstrapDialog`). Per ADR 0016 / main-027,
  the launch-time gate (`IsBootstrapped`) is **strict and version-aware**: it
  delegates to the same helpers the install path uses (`PocketTtsActuallyInstalled`
  / `MockingbirdSidecarActuallyInstalled`) and additionally compares the bundled
  `mockingbird_sidecar/__init__.py`'s `__version__` against the installed copy's
  `__version__`. Any missing wrapper file or version mismatch returns false →
  bootstrap dialog opens → install step re-runs and `File.Copy(overwrite: true)`
  heals the tree. Bumping `__version__` in the bundled `__init__.py` is therefore
  the wrapper's update mechanism — no separate migration code path.
- `BootstrapDialog` drives the bootstrapper with per-step progress, cancel, and retry.
- `StubTtsEngine` is preserved behind `MOCKINGBIRD_USE_STUB_ENGINE=1` for offline /
  CI testing; the env flag also disables the sidecar and bootstrap-dialog wiring.
- `GET /voices` returns the union of the eight pocket-tts built-ins (`alba`, `marius`,
  `javert`, `jean`, `fantine`, `cosette`, `eponine`, `azelma`) with `isBuiltIn: true`
  plus every cloned voice from `library.json` with `isBuiltIn: false`. The union is
  composed by `VoiceCatalog` (main-013) which now consumes `VoiceLibraryService`
  (main-015) alongside the engine. Voice cloning UI lands in main-025; the per-row
  delete affordance ships with main-026.
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

### Cloning panel (main-025)

As of main-025 the Voices page grows a third row beneath the list:
**Clone a new voice**. The panel is always visible (no collapse / expand in
v1), separated from the list above by a thin top border and a `SemiBold`
heading. Its view-model is `VoiceCloningViewModel` — composed into
`VoicesPageViewModel.Cloning`, **not** a separate page.

- **Source toggle** — two `RadioButton`s side by side: Microphone (`Mic24`)
  and System Audio (`Speaker224`). Two-way bound through
  `CloningSourceToBoolConverter` so the radios stay in sync with
  `Cloning.SelectedSource`. Flipping the toggle swaps the device selector
  and the tip line; the recording controls beneath are identical.
- **Mic mode** uses `IAudioCaptureService` (16 kHz mono 16-bit PCM —
  pocket-tts resamples internally per main-015 Q6, so no client-side
  resampling). Default device is `-1` (system default, NAudio maps to 0).
- **System Audio mode** uses `IHighQualityLoopbackService` (WASAPI loopback
  at the native render-endpoint format, typically 48 kHz IEEE-float stereo).
  Capture writes a temp WAV under `%TEMP%\Mockingbird\` for the duration of
  the session; the path is consumed by Save and deleted on success.
- **Recording controls** (shared, identical in both modes per styleguide
  Reusable component map):
  - Audio level meter — horizontal `ProgressBar` driven by the capture
    service's RMS event, dispatched at ~30 Hz.
  - Duration display — `mm:ss` `TextBlock`, updated by a
    `DispatcherTimer` ticking every 33 ms.
  - Progress bar — fills 0..100% across the first 5 s; stays at 100%
    after that (visible "you have enough sample" cue).
  - Voice name input — `MaxLength=40`, validated on every keystroke
    against the same rules as `VoiceLibraryService.AddAsync` (empty,
    >40 chars, sanitised collision with one of the eight built-in ids).
    Inline error under the input.
  - Start / Stop / Cancel / Save Voice buttons. Cancel is only visible
    while capturing; Save is enabled only when **all four** of (capture
    not running) AND (≥5 s captured) AND (name valid) AND (no save in
    flight) hold.
- **Sample length policy**: minimum 5 s (Save disabled until reached),
  soft cap 30 s (status message "You have plenty of audio. You can stop
  now." — capture continues), hard cap 60 s (auto-stop with status
  "Capture auto-stopped at 60 s.").
- **Stop before 5 s** — buffer discarded, status "Recording too short —
  at least 5 s needed." **Cancel** at any point — buffer discarded
  unconditionally.
- **Save flow** (in `SaveAsync`):
  1. Re-validate name client-side (mirror of `VoiceLibraryService` rules).
  2. Render to a temp WAV — mic mode writes float→16-bit PCM to a fresh
     file under `%TEMP%\Mockingbird\`; loopback mode reuses the file the
     capture service already wrote.
  3. Quiet-buffer guard — peak RMS < 0.01 short-circuits with
     "Recording was very quiet. Try again closer to the mic." (no POST).
  4. POST to `/export-voice` via `VoiceCloningClient` (per ADR 0015).
     4xx surfaces as "Pocket-tts couldn't read the recording. …" with the
     response body in dim detail text; 5xx / network as "Voice profile
     encoding failed. See the engine status footer or About page."
  5. Hand the `.safetensors` bytes (and the WAV bytes) to
     `VoiceLibraryService.AddAsync` so the sample lands at
     `<dataPath>\voices\<id>\sample.wav` per ADR 0005.
  6. `LibraryChanged` → `VoicesChanged` → main-014's existing subscription
     refreshes the rows; the new voice appears in the **Cloned** section
     **without page re-navigation**. Form clears, status reads
     "Voice 'Marco' saved."
- **Failure UX** — every error type renders inline above the Save button
  (no toasts, no modals). The captured buffer is **preserved** through
  every failure type; only an explicit Cancel or a successful Save
  clears it, so the user can fix the cause and re-Save without
  re-recording.

The audio-capture services (`IAudioCaptureService`,
`IHighQualityLoopbackService`) are registered **transient** in DI — each
cloning session gets a fresh capture instance.

### Per-row delete affordance (main-026)

Cloned voice rows ship with a per-row Delete button between Preview and the
active-request indicator (built-in rows keep the 3-column layout — Delete is
never offered for the eight pocket-tts built-ins). The button is icon-only
(`Delete24`, `Appearance="Secondary"`, `ToolTip="Delete voice"`), always
visible (not hover-only) per WhisperHeim's consistency rule — the user always
knows the affordance exists.

Click → Fluent **`ui:ContentDialog`** opens via `IContentDialogService`
(registered singleton in DI; MainWindow binds the host `ContentPresenter`
on Loaded). Visual composition matches WhisperHeim's
`DeleteConfirmationDialog`: 40×40 rounded red-tinted icon block (`#20E81224`
background, `#FFE81224` icon), "Delete voice?" title (`Bold`, 16pt), "This
action cannot be undone." subtitle, voice-name card, right-aligned Cancel
(`Secondary`) + Delete (solid `#FFE81224`, white text) buttons. The
ContentDialog `IsFooterVisible="False"` — the dialog body owns its own
button row so we can style the destructive button as red. Cancel / Esc /
click-outside dismiss with no action.

On confirm: `DeleteVoiceDialogViewModel.DeleteAsync` calls
`VoiceLibraryService.DeleteAsync(voiceId, ct)`. On success the dialog hides
itself and the row vanishes via the `LibraryChanged` → `VoicesChanged`
chain that main-014 already wires. On `IOException` (file lock, sidecar
still mmap'ing the `.safetensors` from a just-finished preview) the dialog
**stays open** with an inline red error: "File is locked. Stop playback
and try again." `UnauthorizedAccessException` surfaces the analogous
permission message; any other exception falls through to its `.Message`.
The user can retry Delete or hit Cancel — the dialog is sticky on failure
by design (per task spec, no toast, no auto-dismiss).

Per main-015's delete ordering, `library.json` is pruned **before** the
folder is deleted, so a file-locked folder still removes the row from the
catalog; the orphan folder is cleaned up by `VoiceLibraryStartup`'s
reconciliation on the next launch. Active-playback during delete is **not**
guarded — the synthesis request continues; if the sidecar errors mid-stream
on a now-missing `.safetensors`, the existing `SpeakService` error path
flips the row indicator off and surfaces the error in the status footer.

The page VM's `RequestDelete` constructs a fresh
`DeleteVoiceDialogViewModel` per click (cheap; no shared state to leak),
attaches the dialog instance via `AttachDialog`, and `await`s
`IContentDialogService.ShowAsync`. The Delete column is only allocated on
cloned-row templates (separate `ItemsControl.ItemTemplate` from the
built-in section), so built-in rows keep their original 3-column layout
unmodified.

### Out-of-scope reminders for v1

- No bulk delete / multi-select — single-row only in v1 (main-026).
- No undo — modal confirm is the only safety net.
- No search / filter / tags — vision-deferred until ~15 voices.
- No per-session voice routing UI — env-var-only per main-019.
- No per-row preview error toasts — footer + log file is sufficient signal.
- No "import existing clip from disk" cloning source — backend supports it,
  no UI in v1 (deferred per main-015 Q11).
- No editing of voice metadata (rename, retag), no per-voice quality /
  temperature controls, no de-duplication, no marketplace / sharing.

## Voice library

As of main-015 the cloned-voice **backend** is real (no UI ships in this task —
main-025 builds the cloning sub-flow and main-026 the per-row delete):

- **`VoiceLibraryService`** owns `<dataPath>\voices\*` per ADR 0005. Singleton in
  DI. Every mutation is temp+rename (`profile.safetensors.tmp` → final, then
  `meta.json.tmp` → final, then `library.json.tmp` → final). Order matters: the
  master index (`library.json`) is written **last** so a crash mid-Add leaves a
  recoverable orphan folder rather than an index pointing at nothing. `DeleteAsync`
  inverts the order — index pruned **first**, then the folder — so a file-locked
  folder (e.g. sidecar still has `profile.safetensors` open from a just-finished
  preview) doesn't keep the row visible. One 200 ms retry on `IOException`; if
  the folder still won't delete the warning is logged and startup reconciliation
  cleans up next launch.
- **`VoiceLibraryStartup`** is a hosted-service shim that runs `LoadAsync` once on
  host start so the catalog has cloned rows ready before page VMs resolve.
  Reconciliation is bidirectional: library entries without folders are dropped (with
  a warning + a re-write of `library.json`), folders without entries are reinserted
  from `meta.json` (with a warning), and `meta.json` files declaring
  `schemaVersion > 1` are skipped with a warning rather than crashing.
- **`VoiceCatalog`** subscribes to `VoiceLibraryService.LibraryChanged` and re-fires
  its own `VoicesChanged` so main-014's Voices page refreshes live. `ListAsync`
  returns engine built-ins first, cloned voices second.
- **`VoiceCloningClient`** is the C# side of the sidecar's `POST /export-voice`
  contract. Uploads a sample WAV via multipart, returns the `.safetensors` bytes
  the C# host then hands to `VoiceLibraryService.AddAsync` for persistence. The
  sidecar is stateless between requests — no on-disk profile lives on the Python
  side.
- **Sidecar wrapper module** `mockingbird_sidecar` is bundled next to the binary
  under `src\Mockingbird\PythonSidecar\mockingbird_sidecar\` and copied into the
  Python runtime's site-packages by the bootstrapper (per ADR 0015). It mounts
  `POST /export-voice` and `POST /tts-with-state` on top of pocket-tts's existing
  FastAPI app, and re-exports a `serve` typer command that mirrors
  `pocket_tts serve` — so `SidecarHost.cs`'s spawn argument string only changes
  the module name (`pocket_tts` → `mockingbird_sidecar`).

### Voice profile schema (locked v1)

Per-voice `meta.json`:

```json
{
  "schemaVersion": 1,
  "id": "marco",
  "name": "Marco",
  "engine": "pocket-tts",
  "pocketTtsVersion": null,
  "source": "Mic",
  "createdAt": "2026-05-04T12:34:56+00:00",
  "sampleSeconds": 12,
  "samplePath": "sample.wav",
  "tags": []
}
```

Master `library.json`:

```json
{
  "schemaVersion": 1,
  "voices": [
    { "id": "marco", "name": "Marco", "engine": "pocket-tts",
      "source": "Mic", "createdAt": "2026-05-04T12:34:56+00:00" }
  ]
}
```

The library mirrors a strict subset of `meta.json` so the catalog can populate
the picker without reading N per-voice files. Full per-voice metadata (sample
length, source path, tags, pocket-tts version) lives in `meta.json` only;
catalog consumers that need them can lazy-read on demand.

### Naming rules

- **`id`** — lowercase-kebab, `[a-z0-9-]`, 1–40 chars. Generated from the display
  name by lowercasing, mapping whitespace / `_` / `.` to `-`, dropping anything
  else, and trimming leading/trailing dashes. Stable per voice — never renamed.
- **Built-in collision** — if the generated id matches one of the eight
  built-ins (`alba`, `marius`, `javert`, `jean`, `fantine`, `cosette`, `eponine`,
  `azelma`), `AddAsync` throws `VoiceValidationException` with a "pick a
  different name" message rather than disambiguating. The user explicitly typed
  "Alba" and we won't silently shadow the built-in.
- **Cloned-id collision** — if the id collides with another *cloned* voice or an
  on-disk folder (orphan, ID-typo recovery), a 4-hex `Guid.NewGuid()` suffix
  is appended (`marco`, `marco-a3f2`).
- **Display name** — up to 40 chars after trim + whitespace-collapse; not
  required unique (two voices both called "Marco" coexist via different ids).

### Engine resolution flow

`PocketTtsEngine.StreamAsync(text, voiceId, ct)` branches on whether `voiceId`
matches one of the eight reserved built-in ids (case-insensitive set). If yes,
the existing `/tts` + `voice_url` path runs unchanged. If no, the engine asks
`VoiceLibraryService.TryResolveProfilePath(voiceId)`; null → throws
`InvalidOperationException("Unknown voice id …")` which surfaces through
`SpeakService` to the page status line and to the HTTP `/speak` 500 body. Non-null
→ POST the file as `voice_state` to `/tts-with-state`. The streaming response
shape (24 kHz mono 16-bit PCM after a 44-byte WAV header) is identical for both
endpoints, so the WAV-stripping + chunk-yielding tail of `StreamAsync` is shared.

### What is *not* in main-015

- Cloning UI (mic capture, loopback capture, sample preview) — main-025.
- Per-row delete button + confirmation dialog — main-026.
- File-import path (`source: "Import"`) — backend supports it; no UI in v1.
- Voice rename / re-tag — no v1 task.
- Active-playback guard on delete — delete during preview is allowed; the
  playback cancellation path covers the file-just-disappeared race.
- Tray-warning popup for orphan folders — warning log is enough in v1.

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
