---
id: 0008
title: Cross-cutting — logging, errors, model bootstrap, distribution
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-008, main-009]
related_research: []
---

# ADR 0008: Cross-cutting — logging, errors, model bootstrap, distribution

## Context
Four small concerns that cluster naturally for a personal tool: structured logging, error-surfacing philosophy, first-run model/runtime download UX, and packaging format. The vision is explicit that this is a single-user, no-telemetry tool. WhisperHeim sets a precedent for several of these (Trace logging, single-file self-contained publish).

Each concern is small enough that a dedicated ADR would be heavier than the decision warrants, but together they shape the operational skin of the app and need to be settled before implementation (main-009) lands.

## Decision

### Logging
Adopt **Serilog** with:
- Rolling file sink at `%LOCALAPPDATA%\Utterheim\logs\utterheim-.log`, daily roll, 7-day retention, structured JSON on disk and human-readable console in DEBUG.
- A redirect sink that captures the Python sidecar's stdout/stderr (line-by-line) and writes them with a `sidecar` source enrichment, so sidecar issues show up in the same log stream.
- Log level configurable in `settings.json` (default Information).

### Error philosophy
Fail-loud-to-tray-toast for anything user-visible (sidecar failed to start, voice file corrupt, audio device disconnected, model download stalled). Use `Wpf.Ui.Tray` notifications, not modal dialogs (the user is in their terminal). Everything non-user-visible goes to logs only. **No telemetry, no crash reporting service** — the user is the developer.

### Model + runtime bootstrap
Mirror WhisperHeim's `ModelDefinition` / `ModelDownloadDialog` pattern:
- On first launch, if `%LOCALAPPDATA%\Utterheim\models\pocket-tts\` is empty *or* the bundled Python runtime is incomplete, show a one-shot dialog: "Utterheim needs to download the pocket-tts model (~X MB) and prepare its local Python runtime. Continue?"
- Sequence: prepare embeddable Python → pip install pocket-tts and deps → trigger pocket-tts's own model download → smoke-test a built-in voice.
- Show per-step progress with cancel/retry. Persist a `bootstrap-state.json` so partial progress survives restarts.

### Distribution
v1: `dotnet publish -r win-x64 -c Release --self-contained -p:PublishSingleFile=true` producing a single `utterheim.exe` + a `runtime\python\` folder + asset folders. Distribute as a zip; user extracts and runs. Same approach as WhisperHeim.

## Consequences
### Positive
- All four pieces are minimal-effort and rely on conventions WhisperHeim already validates.
- Sidecar logs and host logs interleave in one searchable stream — invaluable for debugging Python ↔ C# IPC.
- No installer = no signing-cert hassle for v1.
- Bootstrap state persistence keeps a half-finished first run from becoming a dead end.

### Negative
- Zip distribution means no auto-update story; user manually downloads new versions. Deferred to v1.5.
- Self-contained publish + bundled Python is a heavy zip (~hundreds of MB). Acceptable for a personal tool downloaded once.
- Tray-toast errors are easy to miss if the user isn't looking; logs are the backstop.
- No code signing means SmartScreen will warn on first run; user clicks through.

### Neutral
- Serilog adds a transitive dependency footprint, but it's small and proven.
- `%LOCALAPPDATA%\Utterheim\` becomes the durable home for logs, models, runtime, and bootstrap state — uninstall implication noted for v1.5.

## Alternatives considered
- **OpenTelemetry / structured tracing backends** — over-engineered for one user. Deferred indefinitely.
- **Squirrel.Windows / MSIX installer / Velopack** — auto-update, professional feel. Deferred to v1.5; not needed to validate the walking skeleton.
- **Code signing certificate** — useful to silence SmartScreen. Deferred to v1.5; user is developer, will click through.
- **Crash reporter (Sentry / AppCenter)** — vision mandates no telemetry. Skipped.
- **Modal error dialogs instead of tray toasts** — interrupts the terminal-focused workflow Utterheim is built around; tray toasts respect the user's attention.
- **Bundling model + runtime inside the zip** — would balloon download size and force re-download on every update; on-demand bootstrap keeps the binary lean and lets the model live in `%LOCALAPPDATA%`.

## References
- WhisperHeim ModelDownloadDialog: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Views\ModelDownloadDialog.xaml.cs`
- WhisperHeim ModelDefinition: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Services\Models\ModelDefinition.cs`
- Vision: `.agentheim/vision.md`
- ADR 0006 (WhisperHeim reuse — copy-and-modify): `.agentheim/knowledge/decisions/0006-whisperheim-reuse-copy-and-modify.md`
