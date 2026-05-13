---
id: main-025
title: Voice cloning UI — recording controls + source toggle on the Voices page
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-04
commit: 5e66207c0dfef6ff816d4da9f8f2648571582a6c
depends_on: [main-010, main-014, main-015]
blocks: []
tags: [frontend, page, voice-library, audio-capture]
---

## Why

main-015 ships the cloning **backend** (`VoiceLibraryService`, sidecar
`/export-voice`, schema). Without a UI, the user cannot reach it. This
task lands the cloning sub-flow on the Voices page (main-014) per the
styleguide § Reusable component map and WhisperHeim design.md §6
Section B — making cloning the **core differentiator** of the vision
actually visible to the user.

## What

Add a **Clone New Voice** sub-section beneath the Voices list on the
Voices page. The list (built-ins + cloned section, both already
rendered by main-014 / made non-empty by main-015) stays at top; the
cloning controls appear in a collapsible / always-visible panel
below per WhisperHeim design.md §6 Section B (always visible in v1 —
collapsibility is a stretch).

### Layout

Extends main-014's `VoicesPage.xaml`. The page's outer Grid grows a
third row:

```xml
<Grid Margin="16">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- 1. header (main-014) -->
    <RowDefinition Height="*" />     <!-- 2. voices list (main-014) -->
    <RowDefinition Height="Auto" />  <!-- 3. clone-new-voice panel (this task) -->
  </Grid.RowDefinitions>
</Grid>
```

The cloning panel uses a thin top border / `SemiBold` heading "Clone a
new voice" so it reads as a separate concern beneath the list, not as a
list row.

### Source toggle

`SegmentedControl`-style pair (per styleguide § Reusable component map):
two `RadioButton`-styled `ToggleButton`s side by side — **Microphone**
(icon `Mic24`) | **System Audio** (icon `Speaker224`). The mode
swaps the device selector + tip text; the recording controls
themselves are identical in both modes (see styleguide).

### Microphone mode

- **Device selector** — `ui:ComboBox` of input devices from
  `AudioCaptureService.GetAvailableDevices()`. Defaults to system
  default (index `-1` per WhisperHeim's convention).
- **Tip** — small dim `TextBlock`: "Use a quiet environment. The
  recording captures everything the mic hears."

### System Audio mode

- **Device selector** — `ui:ComboBox` of render devices from
  `HighQualityLoopbackService.GetAvailableDevices()`. Defaults to
  current system default render endpoint. (Resolves Q7: default is
  fine for v1, dropdown is there for the user who explicitly wants
  to capture from a non-default device.)
- **Tip** — small dim `TextBlock`: "Close other audio apps and
  play the voice you want to clone. Capture stops when you press
  Stop."

### Recording controls (shared, identical in both modes)

Per styleguide § Reusable component map → "Recording controls" and
WhisperHeim design.md §6 Section B "Shared controls":

- **Audio level meter** — horizontal bar driven by RMS computed in
  the capture service event. Updates on the WPF dispatcher at
  ~30 Hz (every 33 ms). Pulses red while recording per design.md
  "the recording state should be unmistakable".
- **Duration display** — `mm:ss` `TextBlock` updating on the same
  dispatcher tick.
- **Progress bar** — horizontal bar that fills from 0 to 5 s during
  the **minimum-duration** window. Once ≥5 s captured, switches to
  an indeterminate "you can stop now" appearance (or simply caps
  at 100%). Both visually communicate "you have enough sample".
- **Voice name input** — `ui:TextBox` (`MaxLength=40`),
  placeholder "Voice name (e.g. Marco)". Validation matches
  `VoiceLibraryService.AddAsync`'s rules — see "Validation" below.
- **Start / Stop buttons** — `ui:Button` row. Start uses `Mic24`
  icon, `Appearance="Primary"`. Stop uses `Stop24`, `Appearance="Secondary"`,
  enabled only while capturing. (Cancel button — see Q8 below.)
- **Save Voice button** — `ui:Button` with `Save24`, `Appearance="Primary"`,
  enabled when: capture stopped AND `sampleSeconds >= 5` AND voice
  name validates AND no save already in progress. Uses `[RelayCommand]`
  with `IsRunning` driving an inline `ui:ProgressRing` per main-013's
  Save button pattern.

### Sample length policy (resolves Q8)

- **Minimum**: 5 seconds. Save is disabled until reached. The
  WhisperHeim design.md spec says "the meter helps users confirm
  audio is being captured" — Save being disabled until 5 s reached
  is the visible second confirmation.
- **Maximum**: 30 seconds soft cap. After 30 s the controls show
  a non-blocking inline message: "You have plenty of audio. You
  can stop now." Capture continues — the user can keep going up
  to 60 s, after which Stop is auto-fired and a status message
  reads "Capture auto-stopped at 60 s." pocket-tts truncates
  internally so longer samples are wasted bandwidth.
- **Stop button** mid-recording — saves the captured audio if
  ≥5 s, otherwise the in-memory buffer is **discarded** with a
  status message "Recording too short — at least 5 s needed." The
  page returns to the pre-recording state (level meter quiet, Save
  disabled).
- **Cancel button** (small `Appearance="Secondary"`, only visible
  while capturing) — discards the buffer regardless of length and
  returns to pre-recording state. Distinct from Stop because the
  user explicitly says "throw this away."

### Save flow

On Save click:

1. Validate name client-side (same rules as
   `VoiceLibraryService.AddAsync`). Surface inline validation error
   under the name input on failure; don't call backend.
2. Render the captured float-PCM buffer to a temp `.wav` file via
   `NAudio.Wave.WaveFileWriter` (mic mode: 16 kHz mono 16-bit per
   `AudioCaptureService` constants; loopback mode: native format,
   typically 48 kHz IEEE-float stereo per `HighQualityLoopbackService`).
   The sidecar's torchaudio backend resamples internally per main-015
   Q6 — no client-side resampling.
3. POST temp WAV to `utterheim_sidecar`'s `/export-voice`
   (per ADR 0015) via the existing `HttpClient` shape utterheim
   already uses for `/tts`. Display inline progress: "Encoding
   voice profile..." (typically 1–2 s for a 5–20 s sample).
4. Receive `.safetensors` bytes. Call
   `VoiceLibraryService.AddAsync(displayName, source, sampleSeconds,
   profileBytes, sampleBytes)`. The library service writes per
   ADR 0005 layout; fires `LibraryChanged`.
5. `VoiceCatalog.VoicesChanged` fires (catalog re-fires on library
   change per main-015). main-014's existing subscription refreshes
   the rows; the new voice appears in the **Cloned** section
   without re-navigation.
6. Status message: "Voice 'Marco' saved." Voice name input clears,
   buffer clears, controls return to pre-recording state. The user
   can immediately Preview the new row (per main-014's Preview path
   through `SpeakService.Enqueue`).

### Persisting the sample.wav

Pass the captured WAV bytes to `VoiceLibraryService.AddAsync` so it's
written as `<dataPath>\voices\<id>\sample.wav` per ADR 0005.
Rationale: cheap retention (sample is at most ~6 MB at 48 kHz stereo
30 s), enables future re-export with different parameters, lets the
user audit "what audio did I clone from?" via Explorer. Pass `null`
only if a future feature wants to drop samples for size reasons —
not in v1.

### Cloning failure UX (resolves Q9)

Distinct error surfaces inline in the cloning panel — not toasts, not
modals. The panel grows an error region above the Save button when
the save flow fails:

- **Recording too quiet** (peak RMS < 0.01 across the buffer, detected
  client-side before POST) — "Recording was very quiet. Try again
  closer to the mic." Skip the POST entirely.
- **Sidecar 400 (bad input)** — "Pocket-tts couldn't read the
  recording. Check the sample isn't silent or corrupted." Surfaces
  the response body text underneath in dim text for triage.
- **Sidecar 500 / network error / timeout** — "Voice profile
  encoding failed. See the engine status footer or About page."
  Status footer already shows engine state (main-020).
- **`VoiceLibraryService` ValidationException** (name collides with
  built-in / out of length range) — under the name input,
  "{message}".
- **`VoiceLibraryService` IO failure** (disk full, permission) —
  "Couldn't save voice to disk. Check {dataPath} is writable." with
  the path filled in. The temp captured WAV is preserved on disk
  with a deterministic filename so the user could re-try.

The **buffer is preserved** through any failure: the user can adjust
the name and click Save again without re-recording. Only an explicit
Cancel or a successful Save clears it.

### WhisperHeim service copies (per ADR 0006)

This task is the first to bring WhisperHeim's audio-capture services
into utterheim. Per ADR 0006 (copy-and-modify):

| WhisperHeim source | Utterheim target |
|---|---|
| `Services\Audio\IAudioCaptureService.cs` | `Services\Audio\IAudioCaptureService.cs` |
| `Services\Audio\AudioCaptureService.cs` | `Services\Audio\AudioCaptureService.cs` |
| `Services\Audio\IHighQualityLoopbackService.cs` | `Services\Audio\IHighQualityLoopbackService.cs` |
| `Services\Audio\HighQualityLoopbackService.cs` | `Services\Audio\HighQualityLoopbackService.cs` |
| `Services\Audio\LoopbackCaptureService.cs` | **NOT copied** — that's WhisperHeim's downsampled-to-16kHz path for ASR; cloning needs native quality, so only the HQ variant is relevant. |
| `Services\Audio\AudioDeviceInfo.cs` | `Services\Audio\AudioDeviceInfo.cs` |
| `Services\Audio\AudioDeviceResolver.cs` | `Services\Audio\AudioDeviceResolver.cs` |
| `Services\Audio\AudioRingBuffer.cs` | `Services\Audio\AudioRingBuffer.cs` (used by `AudioCaptureService`) |

What gets adapted:

- Namespaces: `WhisperHeim.Services.Audio` → `Utterheim.Services.Audio`.
- `HighQualityLoopbackService.SaveAsVoice(...)` — **delete this method**
  (lines 162–183). It's WhisperHeim's WAV-only persistence; utterheim
  routes through `VoiceLibraryService` which writes both `.safetensors`
  and the sample WAV. The `TempWavFilePath` getter stays — it's how the
  Save flow retrieves the captured audio path.
- `HighQualityLoopbackService.Initialize(DataPathService)` — the
  `CustomVoicesDir` static is no longer relevant; remove it. Utterheim's
  capture writes to `Path.GetTempPath()` only; the persistent location
  is `VoiceLibraryService`'s concern.
- DI: register both as **transient** (each cloning session is a fresh
  capture) under their interfaces.

What stays the same:

- `AudioCaptureService` constants (16 kHz mono 16-bit for mic — fine for
  cloning since pocket-tts resamples internally; no need to bump up).
- `HighQualityLoopbackService` native-format capture (no resampling on
  the way in; sidecar handles it).
- All RMS / level-meter / event plumbing.
- `// Adapted from WhisperHeim/<path> @ <commit>` header on each copied
  file per ADR 0006, plus a CHANGELOG entry.

### Validation

Voice name input enforces (matching `VoiceLibraryService.AddAsync`):

- Empty / whitespace-only → "Enter a name for the voice."
- After trim, > 40 chars → "Name is too long (max 40)."
- Generated id (lowercase-kebab, sanitised) collides with a built-in
  → "That name is reserved. Try a different one."
- Otherwise → valid; Save enables once duration ≥5 s.

Validation runs on every keystroke (`[ObservableProperty]` partial
method `OnVoiceNameChanged`); error surfaces under the input.
Backend `ValidationException` is the safety net but should not fire
in normal use because the UI catches the same conditions.

## Acceptance criteria

- [ ] Voices page (extends main-014) gains a **Clone a new voice**
  section beneath the existing list. Section is always visible in
  v1 (no collapse / expand).
- [ ] Source toggle switches between Microphone and System Audio
  modes. Each mode shows a device selector + a tip line; the
  recording controls beneath are identical in both modes per
  styleguide.
- [ ] WhisperHeim's `AudioCaptureService`, `HighQualityLoopbackService`,
  `AudioDeviceInfo`, `AudioDeviceResolver`, `AudioRingBuffer`, plus
  the two interfaces, are copied into `src\Utterheim\Services\Audio\`
  per ADR 0006. Each file has the `// Adapted from WhisperHeim/...`
  header.
  `LoopbackCaptureService` (the 16 kHz-downsampled variant) is **not**
  copied. `HighQualityLoopbackService.SaveAsVoice` is removed during
  the copy.
- [ ] Live audio level meter responds to mic input within 100 ms of
  speaking. Verified by inspection during dev run.
- [ ] Live audio level meter responds to system-audio loopback within
  100 ms of audio playing on the system default render endpoint.
  Verified by playing a YouTube clip and watching the meter pulse.
- [ ] Duration display updates every ~100 ms during capture.
  Progress bar fills from 0 to 100% over the first 5 s; stays at
  100% (or switches indeterminate) thereafter.
- [ ] Save button is disabled when: not yet captured / captured
  duration < 5 s / voice name fails validation / save already in
  progress. Save enables only when all four pass.
- [ ] Clicking Save with a valid recording + name produces a new
  cloned voice end-to-end in **≤4 s wall-clock**: capture → POST
  to `/export-voice` (≤2 s for a 12 s sample) → write to
  `<dataPath>\voices\<id>\` → `LibraryChanged` → row appears in
  Cloned section. (The 4 s budget includes the encode; the ≤2 s
  budget is for first-chunk-on-Preview, which inherits from main-015
  and ADR 0013.)
- [ ] The new voice's sample is persisted at
  `<dataPath>\voices\<id>\sample.wav`. Verified by Explorer + audio
  player.
- [ ] Stop button before 5 s reached → buffer discarded, status
  message "Recording too short — at least 5 s needed."
  Cancel button (visible only while capturing) → buffer discarded
  unconditionally.
- [ ] After 30 s captured, status message "You have plenty of
  audio. You can stop now." Capture continues. After 60 s, capture
  auto-stops with status "Capture auto-stopped at 60 s."
- [ ] Validation: name input rejects empty / >40 chars / collision
  with a built-in (`alba`/`marius`/`javert`/`jean`/`fantine`/
  `cosette`/`eponine`/`azelma` case-insensitive). Inline error
  under the input; Save disabled while invalid.
- [ ] Failure surfaces (Q9 above): each error type renders inline
  in the cloning panel, not as a toast / modal. The captured buffer
  is preserved through every failure type so the user can re-Save
  after fixing the cause.
- [ ] After a successful Save, the new voice immediately appears
  in the **Cloned** section of the list above (via main-014's
  `VoiceCatalog.VoicesChanged` subscription) **without page
  re-navigation**. Preview button on the new row produces the
  cloned voice within ≤2 s first-chunk per main-015.
- [ ] After a successful Save, the new voice is in the Speak page's
  voice picker (main-013) the next time the page is navigated to
  (or live, since main-013 also subscribes to `VoicesChanged`).
- [ ] Cloning sub-flow is visually nested in the Voices page per
  WhisperHeim design.md §6 Section B — not a separate page, no
  navigation away.
- [ ] Visual matches the styleguide — Mica backdrop, Fluent controls,
  Segoe UI Variable, no bespoke palette. Section heading uses
  `FontWeight="SemiBold"`.
- [ ] Build clean: `dotnet build utterheim.sln -c Debug` produces
  0 errors, 0 warnings.

## Notes

### ADRs that govern this task

- **ADR 0006** — WhisperHeim copy-and-modify (the audio-capture
  services land in utterheim here).
- **ADR 0015** — Utterheim sidecar wrapper (this task POSTs to
  `/export-voice` from the wrapper, not from `pocket_tts.main`).
- **ADR 0010** — `CommunityToolkit.Mvvm` (the cloning panel's
  view-model uses `[ObservableProperty]` / `[RelayCommand]` per the
  page convention).
- **ADR 0005** — voice persistence layout (the sample.wav lands per
  this layout via `VoiceLibraryService`).

### Out of scope (do not creep)

- **Import existing clip from disk** (deferred per main-015 Q11; no
  v1 task).
- **Editing voice metadata** (rename, retag) — no v1 task.
- **Per-voice quality / temperature controls** — pocket-tts defaults.
- **Voice de-duplication** ("you already have a voice that sounds
  like this") — too speculative.
- **Marketplace / sharing** — vision non-goal.

### Worker tips

- The cloning panel's view-model should be a child VM
  (`VoiceCloningViewModel`) composed into `VoicesPageViewModel`,
  not a separate page. Hosts the `IsCapturing`, `Duration`,
  `RmsLevel`, `VoiceName`, `SelectedSource`, `SelectedDevice`
  state.
- For the level meter binding: the capture services raise events
  on the capture thread. Marshal to the WPF dispatcher with
  `Application.Current.Dispatcher.InvokeAsync(...)` (same hop the
  existing `EngineStatusViewModel` uses).
- `WaveFileWriter` for the loopback path: pass the
  `_capture.WaveFormat` directly (already the native format).
  Mic path: same constants as `AudioCaptureService` (16 kHz mono
  16-bit).
- The `[RelayCommand]` for Save should be async and return Task;
  the generated `IsRunning` drives the inline `ProgressRing` for
  free.
- The temp WAV file from `HighQualityLoopbackService.TempWavFilePath`
  should be deleted after Save succeeds. On Save failure, **keep**
  it (so re-Save without re-recording works) and clean up only on
  Cancel or successful Save.
- DI registrations: `IAudioCaptureService` → transient
  `AudioCaptureService`; `IHighQualityLoopbackService` → transient
  `HighQualityLoopbackService`. The page VM resolves them per
  recording session.

## Outcome

The Voices page now has a fully wired **Clone a new voice** sub-section
beneath the existing list — source toggle (Microphone | System Audio),
device selectors per mode, live audio level meter + duration / progress
display, voice-name input with mirror-of-backend validation, and
Start / Stop / Cancel / Save Voice buttons. Save renders the captured
audio to a temp WAV, posts to `/export-voice` via the existing
`VoiceCloningClient`, then persists through `VoiceLibraryService.AddAsync`
so the sample.wav lands per ADR 0005 and `LibraryChanged` re-fires
`VoicesChanged` — the new voice appears in the **Cloned** section without
page re-navigation.

WhisperHeim's audio-capture stack (`IAudioCaptureService`,
`AudioCaptureService`, `IHighQualityLoopbackService`,
`HighQualityLoopbackService`, `AudioDeviceInfo`, `AudioDeviceResolver`,
`AudioRingBuffer`) was copied per ADR 0006 with `// Adapted from
WhisperHeim/<path> @ 911bff0` headers and a CHANGELOG entry per file.
`LoopbackCaptureService` (the 16 kHz-downsampled ASR variant) is
intentionally not copied. `HighQualityLoopbackService.SaveAsVoice` was
deleted, and its `Initialize(DataPathService)` + static `CustomVoicesDir`
were removed — utterheim routes persistence through `VoiceLibraryService`,
not the capture service.

### Verification

`dotnet build utterheim.sln -c Debug` → 0 errors, 0 warnings.

The interactive UI behaviours — level meter pulses on mic + system audio,
progress bar fills 0..5 s, Save enables only when all four conditions
hold, Cancel mid-record discards the buffer, soft cap at 30 s shows
"You have plenty of audio. You can stop now.", hard cap at 60 s
auto-stops, the new voice appears in the Cloned section without
re-navigation — are **not interactively re-tested** in this pass; the
code is in place per the spec and any regression will surface during
the next manual run.

### Key files

- `src\Utterheim\Services\Audio\IAudioCaptureService.cs`
- `src\Utterheim\Services\Audio\AudioCaptureService.cs`
- `src\Utterheim\Services\Audio\IHighQualityLoopbackService.cs`
- `src\Utterheim\Services\Audio\HighQualityLoopbackService.cs`
- `src\Utterheim\Services\Audio\AudioDeviceInfo.cs`
- `src\Utterheim\Services\Audio\AudioDeviceResolver.cs`
- `src\Utterheim\Services\Audio\AudioRingBuffer.cs`
- `src\Utterheim\ViewModels\Pages\VoiceCloningViewModel.cs`
- `src\Utterheim\ViewModels\Pages\VoicesPageConverters.cs`
- `src\Utterheim\ViewModels\Pages\VoicesPageViewModel.cs` (composes Cloning child VM)
- `src\Utterheim\Views\Pages\VoicesPage.xaml` (third row + cloning panel)
- `src\Utterheim\Views\Pages\VoicesPage.xaml.cs` (calls `Cloning.RefreshDevices()` on navigate-to)
- `src\Utterheim\EntryPoint.cs` (DI: transient audio services, transient `VoiceCloningViewModel`)
- `CHANGELOG.md` (created — provenance entries per ADR 0006)
