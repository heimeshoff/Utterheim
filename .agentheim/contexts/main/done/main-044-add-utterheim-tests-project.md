---
id: main-044
title: Add Utterheim.Tests xUnit project — establishes test infrastructure
status: done
type: chore
context: main
created: 2026-05-18
completed: 2026-05-18
commit:
depends_on: []
blocks: [main-040]
tags: [test-infrastructure, tooling, build]
related_adrs: [0001]
related_research: []
prior_art: []
---

## Why
The repository has no test project. Confirmed by the main-040 worker bounce:
`utterheim.sln` lists only `src/Utterheim/Utterheim.csproj` and
`src/Utterheim.Cli/Utterheim.Cli.csproj`, no `*.Tests.csproj`, no
xUnit/NUnit/MSTest PackageReferences anywhere under `src/`.

Worker rule 8 ("no test project" is an explicit bounce condition) means any
task whose acceptance criteria mandate unit tests cannot be picked up.
main-040 (voice-library language field) is the first such task to bounce on
this. Until a test project exists, every future schema/migration/pure-logic
task is blocked the same way.

This task closes that gap. It is deliberately minimal — just enough to make
`dotnet test` run green from day 1 — so it does not foreclose later
conventions and does not bundle in scope that belongs to the first real
test-writing task (which is main-040).

## What

Create `src/Utterheim.Tests/Utterheim.Tests.csproj`:

- **Framework:** xUnit (industry-default for .NET; no specific reason to
  prefer NUnit/MSTest, and matches what the worker note already named).
- **Target:** `net9.0-windows` — matches the host (ADR 0001). The project
  under test is a WPF app; tests may need to construct types that touch
  Windows-only APIs.
- **Platform:** x64 explicitly (matches ADR 0001's RID; avoids "Any CPU"
  surprises when tests touch platform interop).
- **References:** `ProjectReference` to `src/Utterheim/Utterheim.csproj` so
  test code can reach domain types directly.
- **Packages:** the standard xUnit set —
  `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.

Wire it into `utterheim.sln`:
- Add the project to the solution with both `Debug|x64` and `Release|x64`
  build configurations.

Smoke test:
- One trivial `[Fact]` (e.g. `Assert.True(true)`) in a class like
  `SmokeTest.cs` under the project root. The point is purely to prove the
  build/test pipeline works — no production code is exercised in this task.

## Acceptance criteria
- [ ] `src/Utterheim.Tests/Utterheim.Tests.csproj` exists, targets
      `net9.0-windows`, x64 platform.
- [ ] The project references xUnit (`xunit`, `xunit.runner.visualstudio`,
      `Microsoft.NET.Test.Sdk`) and has a `ProjectReference` to
      `src/Utterheim/Utterheim.csproj`.
- [ ] `utterheim.sln` lists the new project, with both `Debug|x64` and
      `Release|x64` build configurations resolved.
- [ ] A single smoke `[Fact]` exists and passes when running
      `dotnet test --configuration Release` from the repository root.
- [ ] `dotnet build --configuration Release` continues to succeed without
      changes to existing projects.

## Notes
- **Do not write tests for existing production code in this task.** This is
  infrastructure only — main-040 is the first real test-writing task and
  will establish the per-test conventions (naming, layout, fixtures). Adding
  speculative tests now would constrain that choice prematurely.
- **No CI today.** v1 distribution is a hand-zip (ADR 0008); there is no
  CI pipeline to update. `dotnet test` runs locally only. A future task can
  wire CI when distribution evolves.
- **WhisperHeim precedent:** worth a quick glance to see whether the sibling
  repo has tests and what framework they use, before settling on xUnit. If
  WhisperHeim is already xUnit, this task aligns with it for free; if it
  uses something else, the worker should flag that mismatch but proceed
  with xUnit unless the user has a strong opinion — there's no reuse-cost
  argument here since test code does not cross repos.
- **No ADR needed.** Test framework choice is tactical and reversible. If a
  later constraint forces a switch, it's a small migration. Codifying xUnit
  in an ADR would be heavier than the decision warrants.
- ADR 0001 link is the only adjacency: x64 platform inheritance.

## Outcome

Added `src/Utterheim.Tests/Utterheim.Tests.csproj` (xUnit, `net9.0-windows`, x64,
RID `win-x64`, `IsPackable=false`) with package references for
`Microsoft.NET.Test.Sdk` 17.12.0, `xunit` 2.9.2, `xunit.runner.visualstudio`
2.8.2, a global `Xunit` `Using`, and a `ProjectReference` to
`src/Utterheim/Utterheim.csproj`. Mirrors the package set and target choices of
WhisperHeim's `WhisperHeim.Tests.csproj` (sibling-repo parity, even though test
code does not cross repos) and omits `coverlet.collector` since no coverage
flow is wired today.

Wired into `utterheim.sln` with GUID `{28A429A6-3B15-4A41-A1B6-0294D3DFB0C8}`
and both `Debug|x64` and `Release|x64` ProjectConfigurationPlatforms entries.

`src/Utterheim.Tests/SmokeTest.cs` holds the single `TestInfrastructureIsWired`
`[Fact]` — pure `Assert.True(true)` infrastructure smoke; no production code is
exercised. The doc-comment explicitly defers real per-domain tests to main-040.

Verified:
- `dotnet build --configuration Release` — all three projects build clean, no
  warnings, no errors.
- `dotnet test --configuration Release --no-build` — 1 test discovered, 1
  passed, 0 failed, 0 skipped.

BC README updated with a `Utterheim.Tests\` entry under the source-layout map,
right after `Utterheim.Cli\`, pointing at `dotnet test --configuration Release`
as the canonical local invocation.

No ADR written — per the task notes, test-framework choice is tactical and
reversible. WhisperHeim already uses xUnit so the parity argument carried it.

Key files:
- `C:\src\heimeshoff\tooling\utterheim\src\Utterheim.Tests\Utterheim.Tests.csproj`
- `C:\src\heimeshoff\tooling\utterheim\src\Utterheim.Tests\SmokeTest.cs`
- `C:\src\heimeshoff\tooling\utterheim\utterheim.sln`
- `C:\src\heimeshoff\tooling\utterheim\.agentheim\contexts\main\README.md`

