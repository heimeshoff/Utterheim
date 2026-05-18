---
id: main-040
title: Voice library — add language field; populate built-ins including `juergen`
status: done
type: feature
context: main
created: 2026-05-18
completed: 2026-05-18
commit: 448fce6
depends_on: [main-035, main-044]
blocks: [main-039, main-041]
tags: [multilingual, voice-library, schema, persistence]
related_adrs: [0005, 0023]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-005, main-015]
---

## Why
ADR for `main-035` decides that each voice profile carries its own language.
The voice library — the on-disk `library.json` plus the in-memory model
behind the Voices page — currently has no language column. Adding it is the
prerequisite for the sidecar routing in `main-039` and the UI work in
`main-041`.

We also want the built-in German voice `juergen` (introduced in pocket-tts
2.1.0 to avoid English-accented German output) listed alongside the English
built-ins.

## What

### Schema change
Extend the voice profile structure to include a `language` field. Allowed
values are the pocket-tts language identifiers in use:

- `english` (today's implicit default for every existing voice)
- `german`
- (room to grow: `french`, `italian`, `spanish`, `portuguese` later)

`library.json` migration: on load, any voice entry without a `language` field
is treated as `english`. The migration runs in-place — on next save, the
field is persisted. No version bump required if the field is optional with a
documented default; the worker may also opt to bump a schema version if
that's how the existing file is structured (check current shape first per
ADR 0005).

### Built-in voices
Extend the built-in voice list to include `juergen` (German). The existing
English built-ins (alba, marius, etc.) stay tagged `english`. No other
language defaults are added in this task — German + English only, matching
the preload decision in ADR for `main-036`.

### Service surface
`VoiceLibraryService` exposes `Language` on each `Voice` entity it returns.
The cloning path (`/export-voice` via the sidecar) accepts and persists the
target language when a clone is created — this is the wiring the UI in
`main-041` will drive.

## Acceptance criteria
- [ ] Voice profile schema includes a `language` field with values from a
      typed enum (or equivalent constrained string set). `english` and
      `german` both representable.
- [ ] Loading a `library.json` that pre-dates this change does not error;
      every existing voice is presented as `english`. On next save, the
      field is written.
- [ ] Built-in voice list contains `juergen` tagged `german`, plus the
      existing English built-ins tagged `english`.
- [ ] `VoiceLibraryService.AddCloned(...)` (or equivalent) accepts the
      target language and persists it on the new voice's folder/index entry.
- [ ] Unit tests cover: legacy file loads as english; new voice persists
      its language; built-in `juergen` enumerates with `german`.
- [ ] No behaviour change in the speak path yet — `main-039` consumes this.

## Notes
The on-disk layout (folder-per-voice + `library.json` index, ADR 0005) is
the right place to add the field — either as a per-voice attribute in
`library.json`, or as a per-voice `voice.json` next to the `.safetensors`
file. The worker picks based on what fits the current code; the ADR doesn't
prescribe.

The research's table in section 2 lists every language's built-in voice
(estelle/juergen/giovanni/lola/rafael) — we're only adding `juergen` here.
Adding the others later is a one-line config change once the schema exists.

The user's earlier feedback on Voices page ordering (cloned above built-in,
done in `main-033`) is unrelated to this task — language is a *property* of
a voice, not a grouping axis on the page. The UI grouping in `main-041` is
separate.

`prior_art: main-005` is the persistence-layout ADR task; `main-015` is the
voice-cloning backend task — both touched the same code surface.

## Worker note (2026-05-18, bounced)

This repository has **no test project**. Confirmed by:
- `utterheim.sln` lists only `src/Utterheim/Utterheim.csproj` and
  `src/Utterheim.Cli/Utterheim.Cli.csproj`. No `*.Tests.csproj`.
- Glob across the tree for `**/*Tests*.csproj`, `**/*.Tests.csproj`,
  `**/*test*.csproj`, `**/xunit*`, plus a content grep for `[Fact]`,
  `[Test]`, `xunit`, `nunit`, `mstest` over `src/` all return empty.
- The Utterheim.csproj has no test-framework PackageReferences.

Acceptance criterion 5 ("Unit tests cover: legacy file loads as english;
new voice persists its language; built-in `juergen` enumerates with
`german`") is therefore not satisfiable in-place. Per worker rule 8 —
"no test project" is an explicit bounce condition — this task is being
moved back to backlog.

**Recommended next step before re-dispatching main-040:** create a
preceding tactical task (e.g. `main-044 — Add Utterheim.Tests xUnit
project`) that:
- Adds `src/Utterheim.Tests/Utterheim.Tests.csproj` (xUnit, targeting
  `net9.0-windows`, ProjectReference to Utterheim, x64 platform).
- Wires it into `utterheim.sln`.
- Includes one trivial smoke test (`Assert.True(true)`) so `dotnet test`
  runs green from CI on day 1.

Once that project exists, main-040 becomes straightforward and the worker
can follow the three named test cases under TDD as the task requires.

Alternative (lower-fidelity) path: the user could relax acceptance
criterion 5 to "manual verification with a hand-edited legacy library.json"
and waive the unit-test requirement on this task. That would unblock
main-039/main-041 immediately but leaves the regression risk to a future
refactor.

Note: the implementation itself is well-specified and small — a Language
enum (English, German) on `ClonedVoiceMeta` + `ClonedVoiceIndexEntry`,
defaulting to English in init for legacy-file compat; a Language overload
on `VoiceLibraryService.AddAsync`; a `juergen` entry in `BuiltInVoices`
with language=German; and a `Language` field on `VoiceDescriptor`. The
schema-shape question (ADR 0026 placeholder mentioned by the orchestrator)
resolves trivially: keep language on the existing per-voice attribute in
both `library.json` and `meta.json`, mirroring what `Engine` and `Source`
already do — no new file, no ADR needed.

## Refinement (2026-05-18)

User chose path (a) from the bounce note: precede with a test-project
task rather than relax AC 5. Captured `main-044 — Add Utterheim.Tests
xUnit project` (`todo/main-044-add-utterheim-tests-project.md`).

- `depends_on` updated to `[main-035, main-044]`.
- Re-promote main-040 from backlog to todo once main-044 ships.
- AC 5 stays as-is — the three named unit tests remain mandatory.
- Worker note above is kept as historical context.

## Outcome

Schema-shape decision matched the worker note's recommendation: `language` is
a per-voice attribute on the existing `meta.json` + `library.json` records,
mirroring how `engine` and `source` are already structured (no new file, no
`schemaVersion` bump — the field is additive with a documented default).

Code:
- `src\Utterheim\Services\Voices\ClonedVoiceMeta.cs` — new `VoiceLanguage`
  enum (`English`, `German`) decorated with `[JsonStringEnumMemberName]` so
  it serialises lower-case (`"english"`, `"german"`) per ADR 0023. Added a
  `Language` init-only property on both `ClonedVoiceMeta` and
  `ClonedVoiceIndexEntry`, defaulting to `English` so legacy on-disk files
  load as English without ceremony.
- `src\Utterheim\Services\Voices\VoiceLibraryService.cs` — `AddAsync` gains
  an optional `language` parameter (default `English`), persisted into both
  the per-voice `meta.json` and the `library.json` index row. The orphan-
  reinsertion path in `LoadAsync` mirrors `meta.json`'s language onto the
  rebuilt index entry. `juergen` added to `ReservedBuiltInIds` so a user
  clone named "Juergen" is rejected rather than silently shadowing.
- `src\Utterheim\Services\Tts\ITtsEngine.cs` — `VoiceDescriptor` record
  extended with `Language`. The `Engine`/`Source` pattern dictated the
  positional placement.
- `src\Utterheim\Services\Tts\PocketTtsEngine.cs` — `BuiltInVoices` extended
  with `("juergen", "Juergen", "pocket-tts", true, VoiceLanguage.German)`;
  the eight English voices keep their tag.
- `src\Utterheim\Services\Speak\VoiceCatalog.cs` — cloned-voice descriptor
  construction now propagates `Language` from the index entry to the
  catalog row main-039's sidecar map will consume.
- `src\Utterheim\Services\Tts\StubTtsEngine.cs` — test-tone voice keeps the
  shape consistent (`VoiceLanguage.English`).

Tests in `src\Utterheim.Tests\Voices\`:
- `VoiceLibraryLanguageTests.cs` — four facts: legacy `library.json` loads
  as english, legacy `meta.json` loads as english, `AddAsync` with German
  persists the field in both files + the in-memory listing, and
  `AddAsync` without an arg defaults to English. The Add tests round-trip
  through real disk under a per-test `%TEMP%\Utterheim.Tests\<guid>\`
  folder, driven by a small `TempDataPath` helper that injects the
  `DataPathService._bootstrap` field via reflection so the test never
  writes to the user's real `%APPDATA%\Utterheim\bootstrap.json`.
- `BuiltInVoicesTests.cs` — two facts: `juergen` enumerates with
  `VoiceLanguage.German`, and the eight English built-ins keep their
  English tag.

What was deliberately NOT changed:
- The speak request body — still `{text, voice}` per ADR 0023. The sidecar
  voice→language map and the routing decision are main-039.
- `VoiceCloningViewModel` — keeps its current `AddAsync` call; the
  language picker UI is main-041. The optional-arg default on `AddAsync`
  keeps that call site silent until then.
- Other languages (french / italian / spanish / portuguese) — pocket-tts
  2.1.0 has built-ins for all of them, but per ADR 0024 we only preload
  english + german at this stage, so the enum is intentionally narrow.

Verification:
- `dotnet build utterheim.sln --configuration Release` → 0 warnings, 0 errors.
- `dotnet test src/Utterheim.Tests --configuration Release` → 7/7 pass
  (1 smoke + 6 new).
