---
id: 0006
title: Reuse WhisperHeim infrastructure via copy-and-modify in v1
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-006, main-009]
related_research: []
---

# ADR 0006: Reuse WhisperHeim infrastructure via copy-and-modify in v1

## Context
WhisperHeim has battle-tested infrastructure utterheim needs: `GlobalHotkeyService` (low-level keyboard hook, double-tap-friendly), `IAudioCaptureService` / `HighQualityLoopbackService` (mic + WASAPI loopback for sample capture), `DataPathService` / `SettingsService` (path conventions + JSON persistence), `StartupService` (run-at-login), and the tray-shell + Wpf.Ui aesthetic. None of these services are currently packaged as a library; they live inside `WhisperHeim.Services.*` and depend on WhisperHeim's `Models.*` types and naming.

The two apps will diverge in concrete details (hotkey gesture, capture lifecycle, file layout names) but share the underlying Win32/WASAPI primitives.

## Decision
For v1, **copy-and-modify** the relevant source files into utterheim's tree:
- Rewrite namespaces (`WhisperHeim.Services.X` → `Utterheim.Services.X`).
- Rename WhisperHeim-specific path strings ("WhisperHeim" → "Utterheim") in `DataPathService`.
- Retain a `// Adapted from WhisperHeim/<path> @ <commit>` header on each copied file plus a one-line entry in utterheim's CHANGELOG so we don't lose the provenance.
- Diverge freely: e.g., add a "double-tap detector" wrapper around `GlobalHotkeyService` for the LCtrl gesture without bothering WhisperHeim.

After both apps ship v1 and the surface stabilises, extract the truly-shared services into a `Heimeshoff.Audio` / `Heimeshoff.Hotkeys` shared library (or NuGet feed). Plan that as a separate decision, not v1.

## Consequences
### Positive
- Fastest path to a walking skeleton.
- Either app can evolve its copy without coordinating with the other.
- Easy to read: every file's origin is annotated.

### Negative
- Bug fixes won't propagate automatically — when WhisperHeim fixes a hotkey edge case, utterheim needs a manual port (and vice versa). Acceptable for one developer; not acceptable forever.
- Two copies risk drift in subtle behaviour (e.g., audio sample rate normalisation).

### Neutral
- The utterheim-specific double-tap detector becomes a candidate to upstream into WhisperHeim (or vice-versa) once the shared library exists.

## Alternatives considered
- **Shared library extracted up-front** — rejected: WhisperHeim's services are not library-shaped today. Untangling them is a project of its own and would block utterheim's walking skeleton.
- **Git submodule pointing at WhisperHeim** — rejected: shared *source* coupling without shared *deployment*; version drift between the two repos' pointers; rituals on every checkout. Worst of both worlds.

## References
- WhisperHeim source: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\`
- Vision: `.agentheim/vision.md`
- Follow-up task: `main-009` (actual file copy operation)
