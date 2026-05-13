---
id: main-008
title: Cross-cutting — Serilog, fail-loud-to-tray, model bootstrap, zip distribution
status: done
type: decision
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: []
blocks: [main-009]
tags: [foundation, logging, errors, distribution]
---

## Why

Four small concerns cluster naturally for a personal tool: structured logging, error-surfacing philosophy, first-run model+runtime bootstrap UX, and packaging format. Each is small enough that emitting four separate ADRs would be heavier than the decisions warrant.

## What

- **Logging**: Serilog with rolling file sink at `%LOCALAPPDATA%\Utterheim\logs\utterheim-.log` (daily roll, 7-day retention). A redirect sink captures the Python sidecar's stdout/stderr line-by-line as `sidecar`-tagged log events.
- **Error philosophy**: Fail-loud to `Wpf.Ui.Tray` toast for user-visible failures. Logs for everything else. No telemetry, no crash reporter.
- **Model + runtime bootstrap**: On first launch (or whenever `%LOCALAPPDATA%\Utterheim\models\pocket-tts\` is empty / runtime incomplete), show a one-shot dialog mirroring WhisperHeim's `ModelDownloadDialog`. Sequence: prepare embeddable Python → pip install pocket-tts and deps → trigger pocket-tts model download → smoke-test. Persist `bootstrap-state.json` so partial progress survives restarts.
- **Distribution**: v1 ships as a self-contained single-file zip via `dotnet publish -r win-x64 -c Release --self-contained -p:PublishSingleFile=true` plus a `runtime\python\` sibling folder. Same pattern as WhisperHeim. Auto-update / installer / signing deferred to v1.5.

## Acceptance criteria

- [ ] ADR 0008 committed at `.agentheim/knowledge/decisions/0008-cross-cutting-concerns.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries user amendments).
- [ ] No code yet — implementation lands in main-009.

## Notes

v1.5 candidates explicitly deferred:
- Velopack / Squirrel / MSIX auto-update.
- Code-signing certificate for SmartScreen.
- Crash reporter (rejected by vision).
- One-click uninstall entry that wipes `%LOCALAPPDATA%\Utterheim\` and optionally the `<dataPath>\voices\` folder.

Full ADR draft (drop into `0008-cross-cutting-concerns.md`):

```markdown
# ADR 0008: Cross-cutting — logging, errors, model bootstrap, distribution

## Context
Four small concerns that cluster naturally for a personal tool: structured logging, error-surfacing philosophy, first-run model/runtime download UX, and packaging format. The vision is explicit that this is a single-user, no-telemetry tool. WhisperHeim sets a precedent for several of these (Trace logging, single-file self-contained publish).

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
v1: `dotnet publish -r win-x64 -c Release --self-contained -p:PublishSingleFile=true` producing a single utterheim.exe + a `runtime\python\` folder + asset folders. Distribute as a zip; user extracts and runs. Same approach as WhisperHeim.

## Consequences
### Positive
- All four pieces are minimal-effort and rely on conventions WhisperHeim already validates.
- Sidecar logs and host logs interleave in one searchable stream — invaluable for debugging Python ↔ C# IPC.
- No installer = no signing-cert hassle for v1.

### Negative
- Zip distribution means no auto-update story; user manually downloads new versions. Deferred to v1.5.
- Self-contained publish + bundled Python is a heavy zip (~hundreds of MB). Acceptable for a personal tool downloaded once.

### Neutral
- Serilog adds a transitive dependency footprint, but it's small and proven.

## Alternatives considered (and deferred)
- **OpenTelemetry** — over-engineered for one user. Deferred indefinitely.
- **Squirrel.Windows / MSIX installer / Velopack** — auto-update, professional feel. Deferred to v1.5; not needed to validate the walking skeleton.
- **Code signing** — useful to silence SmartScreen. Deferred to v1.5; user is developer, will click through.
- **Crash reporter (Sentry/AppCenter)** — vision mandates no telemetry. Skipped.

## References
- WhisperHeim ModelDownloadDialog: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Views\ModelDownloadDialog.xaml.cs`
- WhisperHeim ModelDefinition: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Services\Models\ModelDefinition.cs`
- Vision: `.agentheim/vision.md`
```

## Outcome

ADR 0008 written at `.agentheim/knowledge/decisions/0008-cross-cutting-concerns.md` (`scope: global`, `status: accepted`). Captures the four cross-cutting decisions: Serilog rolling-file logging with a sidecar redirect sink, fail-loud-to-tray-toast error philosophy with no telemetry, WhisperHeim-style first-run model + Python runtime bootstrap dialog with persisted state, and v1 distribution as a self-contained single-file zip via `dotnet publish`. Auto-update, code signing, and crash reporting explicitly deferred. Implementation will follow in main-009.

Key files:
- `.agentheim/knowledge/decisions/0008-cross-cutting-concerns.md`
