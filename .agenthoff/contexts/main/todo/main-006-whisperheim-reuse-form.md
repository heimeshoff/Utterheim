---
id: main-006
title: Reuse WhisperHeim infrastructure via copy-and-modify in v1
status: todo
type: decision
context: main
created: 2026-05-01
completed:
commit:
depends_on: []
blocks: [main-009]
tags: [foundation, reuse, whisperheim]
---

## Why

Mockingbird needs WhisperHeim's audio capture, hotkeys, settings/path, startup, and tray-shell infrastructure. None of those services are currently library-shaped — they live in `WhisperHeim.Services.*` with WhisperHeim-specific names. Choosing the reuse form (shared lib / copy-and-modify / submodule) up front avoids a half-extracted limbo state during walking-skeleton work.

## What

**Copy-and-modify for v1.** Copy the relevant source files into mockingbird's tree, rewrite namespaces to `Mockingbird.Services.*`, retain `// Adapted from WhisperHeim/<path> @ <commit>` headers and a CHANGELOG entry per copied file. After both apps ship v1 and surfaces stabilise, extract truly-shared services into `Heimeshoff.Audio` / `Heimeshoff.Hotkeys` shared projects.

Files in scope to copy first:

- Hotkey: `GlobalHotkeyService`, `HotkeyRegistration`, `NativeMethods` (under `Services/Hotkey/`)
- Audio: `IAudioCaptureService`, `AudioCaptureService`, `IHighQualityLoopbackService`, `HighQualityLoopbackService`, `AudioRingBuffer`, `AudioDeviceInfo`, `AudioDeviceResolver` (under `Services/Audio/`)
- Settings/path: `DataPathService`, `SettingsService` (rename WhisperHeim → Mockingbird in path strings)
- Startup: `StartupService` (run-at-login)

## Acceptance criteria

- [ ] ADR 0006 committed at `.agenthoff/knowledge/decisions/0006-whisperheim-reuse-copy-and-modify.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries user amendments).
- [ ] No code yet — actual file copy lands in main-009.

## Notes

Submodule was ruled out: shared *source* coupling without shared *deployment* semantics; version drift between repos' submodule pointers; ritual on every checkout. Worst of both worlds.

Shared-library-up-front was ruled out: WhisperHeim's services are not yet library-shaped; extracting them is its own project; would block mockingbird's walking skeleton.

Open follow-up: a small script that diffs copied files against WhisperHeim originals to flag drift. Not v1.

Full ADR draft (drop into `0006-whisperheim-reuse-copy-and-modify.md`):

```markdown
# ADR 0006: Reuse WhisperHeim infrastructure via copy-and-modify in v1

## Context
WhisperHeim has battle-tested infrastructure mockingbird needs: `GlobalHotkeyService` (low-level keyboard hook, double-tap-friendly), `IAudioCaptureService` / `HighQualityLoopbackService` (mic + WASAPI loopback for sample capture), `DataPathService` / `SettingsService` (path conventions + JSON persistence), `StartupService` (run-at-login), and the tray-shell + Wpf.Ui aesthetic. None of these services are currently packaged as a library; they live inside `WhisperHeim.Services.*` and depend on WhisperHeim's `Models.*` types and naming.

The two apps will diverge in concrete details (hotkey gesture, capture lifecycle, file layout names) but share the underlying Win32/WASAPI primitives.

## Decision
For v1, **copy-and-modify** the relevant source files into mockingbird's tree:
- Rewrite namespaces (`WhisperHeim.Services.X` → `Mockingbird.Services.X`).
- Rename WhisperHeim-specific path strings ("WhisperHeim" → "Mockingbird") in `DataPathService`.
- Retain a `// Adapted from WhisperHeim/<path> @ <commit>` header on each copied file plus a one-line entry in mockingbird's CHANGELOG so we don't lose the provenance.
- Diverge freely: e.g., add a "double-tap detector" wrapper around `GlobalHotkeyService` for the LCtrl gesture without bothering WhisperHeim.

After both apps ship v1 and the surface stabilises, extract the truly-shared services into a `Heimeshoff.Audio` / `Heimeshoff.Hotkeys` shared library (or NuGet feed). Plan that as a separate decision, not v1.

## Consequences
### Positive
- Fastest path to a walking skeleton.
- Either app can evolve its copy without coordinating with the other.
- Easy to read: every file's origin is annotated.

### Negative
- Bug fixes won't propagate automatically — when WhisperHeim fixes a hotkey edge case, mockingbird needs a manual port (and vice versa). Acceptable for one developer; not acceptable forever.
- Two copies risk drift in subtle behaviour (e.g., audio sample rate normalisation).

### Neutral
- The mockingbird-specific double-tap detector becomes a candidate to upstream into WhisperHeim (or vice-versa) once the shared library exists.

## Alternatives considered
- **Shared library extracted up-front** — rejected: WhisperHeim's services are not library-shaped today. Untangling them is a project of its own and would block mockingbird's walking skeleton.
- **Git submodule pointing at WhisperHeim** — rejected: shared *source* coupling without shared *deployment*; version drift between the two repos' pointers; rituals on every checkout. Worst of both worlds.

## References
- WhisperHeim source: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\`
- Vision: `.agenthoff/vision.md`
```
