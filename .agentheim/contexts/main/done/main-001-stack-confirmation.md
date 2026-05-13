---
id: main-001
title: Confirm and document the .NET 9 / WPF / WPF-UI / Win x64 stack
status: done
type: decision
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: []
blocks: [main-009]
tags: [foundation, stack]
---

## Why

Utterheim inherits its UI tech and design language from WhisperHeim. Before any code lands, the stack should be explicitly committed to as an ADR — both so the worker has an unambiguous starting point and so future revisits know what we considered.

## What

Adopt the WhisperHeim stack as-is. Produce ADR `0001-stack-net9-wpf-x64.md` under `.agentheim/knowledge/decisions/` recording the choice and the alternatives.

## Acceptance criteria

- [ ] ADR 0001 committed at `.agentheim/knowledge/decisions/0001-stack-net9-wpf-x64.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries the user's amendments).
- [ ] `utterheim.csproj` is created with `net9.0-windows`, `UseWPF=true`, `Platforms=x64`, `RuntimeIdentifier=win-x64`, `OutputType=WinExe`, server GC enabled, `Nullable` and `ImplicitUsings` on. (Project skeleton is part of the walking skeleton task — this ADR just documents the choice.)
- [ ] No code change beyond the ADR file itself.

## Notes

ADR draft from the architecture foundation pass:

```markdown
# ADR 0001: Adopt .NET 9 / WPF / WPF-UI / Windows x64 stack

## Context
Utterheim is the TTS sibling of WhisperHeim and inherits its UI tech and design
language ("feels like a first-party Windows app — Mica, Fluent, Segoe UI Variable").
WhisperHeim already runs on .NET 9 + WPF + WPF-UI 3.x + NAudio, x64-only, with server
GC and `net9.0-windows` target. The two apps deploy independently but should look,
feel, and reuse infrastructure.

## Decision
Utterheim uses the same stack as WhisperHeim:
- `<TargetFramework>net9.0-windows</TargetFramework>`, `<UseWPF>true</UseWPF>`
- `<Platforms>x64</Platforms>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
- `<OutputType>WinExe</OutputType>` + Wpf.Ui.Tray for the tray shell
- NAudio for WASAPI loopback / playback
- Nullable + ImplicitUsings on, server GC on
- Minimum OS: Windows 10 22H2; primary target: Windows 11

## Consequences
### Positive
- Direct copy/reference of WhisperHeim's audio plumbing, hotkey hook, tray shell.
- Shared aesthetic (Mica backdrop, Fluent controls) "for free" via WPF-UI.
- Onboarding path identical to WhisperHeim (same brain, one developer).

### Negative
- Inherits WPF's quirks (XAML compile, no cross-platform).
- x64-only excludes Windows-on-ARM until we revisit.

### Neutral
- WPF-UI 3.x version pinning matters; track WhisperHeim's version to keep parity.

## Alternatives considered
- **Avalonia / .NET 9** — cross-platform, modern. Rejected: no existing infra to reuse, no Mica out of the box, breaks parity with WhisperHeim.
- **WinUI 3 / Windows App SDK** — modern Microsoft path. Rejected: WhisperHeim is on WPF, so re-platforming buys nothing for v1; revisit once both apps need a refresh.
- **Electron / web** — rejected: privacy story, audio plumbing, tray polish all worse.

## References
- WhisperHeim csproj: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\WhisperHeim.csproj`
- Vision: `.agentheim/vision.md`
```

## Outcome

Stack decision recorded as ADR 0001 (`scope: global`, `status: accepted`).
Utterheim will mirror WhisperHeim: .NET 9 + WPF + WPF-UI 3.x + NAudio,
x64-only, server GC, `net9.0-windows`. The csproj/skeleton itself is created
by the walking-skeleton task (main-009); this task only commits the choice.

- ADR: `.agentheim/knowledge/decisions/0001-stack-net9-wpf-x64.md`

