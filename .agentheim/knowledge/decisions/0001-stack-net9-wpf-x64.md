---
id: 0001
title: Adopt .NET 9 / WPF / WPF-UI / Windows x64 stack
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-001]
related_research: []
---

# ADR 0001: Adopt .NET 9 / WPF / WPF-UI / Windows x64 stack

## Context

Mockingbird is the TTS sibling of WhisperHeim and inherits its UI tech and
design language ("feels like a first-party Windows app — Mica, Fluent, Segoe
UI Variable"). WhisperHeim already runs on .NET 9 + WPF + WPF-UI 3.x + NAudio,
x64-only, with server GC and a `net9.0-windows` target. The two apps deploy
independently but should look, feel, and reuse infrastructure. A single
developer maintains both, so onboarding cost and mental-model overlap matter.

## Decision

Mockingbird uses the same stack as WhisperHeim:

- `<TargetFramework>net9.0-windows</TargetFramework>`, `<UseWPF>true</UseWPF>`
- `<Platforms>x64</Platforms>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
- `<OutputType>WinExe</OutputType>` plus Wpf.Ui.Tray for the tray shell
- NAudio for WASAPI loopback / playback
- `Nullable` and `ImplicitUsings` on, server GC on
- Minimum OS: Windows 10 22H2; primary target: Windows 11

## Consequences

### Positive

- Direct copy / reference of WhisperHeim's audio plumbing, hotkey hook, and
  tray shell.
- Shared aesthetic (Mica backdrop, Fluent controls) "for free" via WPF-UI.
- Onboarding path identical to WhisperHeim (same brain, one developer).

### Negative

- Inherits WPF's quirks (XAML compile, no cross-platform).
- x64-only excludes Windows-on-ARM until we revisit.

### Neutral

- WPF-UI 3.x version pinning matters; track WhisperHeim's version to keep
  parity.

## Alternatives considered

- **Avalonia / .NET 9** — cross-platform, modern. Rejected: no existing infra
  to reuse, no Mica out of the box, breaks parity with WhisperHeim.
- **WinUI 3 / Windows App SDK** — modern Microsoft path. Rejected: WhisperHeim
  is on WPF, so re-platforming buys nothing for v1; revisit once both apps
  need a refresh.
- **Electron / web** — rejected: privacy story, audio plumbing, and tray
  polish are all worse.

## References

- WhisperHeim csproj: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\WhisperHeim.csproj`
- Vision: `.agenthoff/vision.md`
