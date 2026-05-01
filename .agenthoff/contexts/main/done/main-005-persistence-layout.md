---
id: main-005
title: Voice profiles as folder-per-voice + library.json index
status: done
type: decision
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: []
blocks: [main-009]
tags: [foundation, persistence, voice-library]
---

## Why

Voice profiles are `.safetensors` kvcache files plus metadata. Vision target: 5–10 voices in v1, growing to 20–50. SQLite would be overkill. A clear filesystem layout decision now removes friction in main-009 (walking skeleton) and main-010 (voice CRUD).

## What

Filesystem-folder-per-voice + a single `library.json` index file. Mirror WhisperHeim's `DataPathService` pattern: bootstrap in `%APPDATA%\Mockingbird`, optional remappable data path (so the user can sync `voices\` via OneDrive), runtime + models in `%LOCALAPPDATA%\Mockingbird` (machine-local, never synced).

Layout:

```
%APPDATA%\Mockingbird\
  bootstrap.json
  settings.json
  logs\

<dataPath>\                    (default = %APPDATA%\Mockingbird; override via bootstrap.json)
  voices\
    library.json               (master index)
    <voice-id>\
      profile.safetensors
      meta.json
      sample.wav               (optional)

%LOCALAPPDATA%\Mockingbird\
  runtime\python\              (bundled embeddable Python + venv)
  models\pocket-tts\           (model weights)
  cache\
```

## Acceptance criteria

- [ ] ADR 0005 committed at `.agenthoff/knowledge/decisions/0005-voice-persistence-layout.md` with `scope: global`.
- [ ] ADR matches the draft in Notes (or carries user amendments).
- [ ] No code yet — implementation lands in main-009.
- [ ] `meta.json` schema is sketched in the ADR (fields: `name`, `engine`, `pocketTtsVersion`, `source` (mic/loopback/import), `createdAt`, `tags`, optional `samplePath`).

## Notes

`library.json` is authoritative for "what voices does the user have." On startup, reconcile with folder contents: orphan folders or orphan entries surface a tray warning, never silently dropped.

Atomic JSON writes: write-temp-then-rename to avoid torn writes.

Migration path to SQLite: easy if voice count exceeds ~200 or query patterns demand it. Not v1.

Full ADR draft (drop into `0005-voice-persistence-layout.md`):

```markdown
# ADR 0005: Voice profiles as folder-per-voice + library.json index

## Context
Voice profiles are `.safetensors` kvcache files plus metadata (name, source, dates, sample reference, engine tag). Vision target: 5–10 voices in v1, growing to 20–50. WhisperHeim's `DataPathService` already establishes a layered path convention (bootstrap in `%APPDATA%`, optional remapped data dir for OneDrive sync, runtime/models in `%LOCALAPPDATA%`). Settings persistence in WhisperHeim is plain JSON.

## Decision
- One folder per voice under `<dataPath>\voices\<voice-id>\` containing `profile.safetensors`, `meta.json`, and optionally `sample.wav`.
- A single `library.json` at `<dataPath>\voices\library.json` holding the master index (id → display name, engine, source, created, tags). Loaded on startup, reconciled against actual folder contents (orphan folders / missing entries surface a tray warning, never silently dropped).
- Settings live in `<dataPath>\settings.json` (synced) and bootstrap `%APPDATA%\Mockingbird\bootstrap.json` (machine-local pointer to the data path). Same pattern as WhisperHeim.
- Pocket-tts model weights and the bundled Python runtime live in `%LOCALAPPDATA%\Mockingbird\` (machine-local, not synced).

`meta.json` schema (sketch):

```json
{
  "id": "alba-clone-001",
  "name": "Alba (cloned)",
  "engine": "pocket-tts",
  "pocketTtsVersion": "2.0.0",
  "source": "loopback",
  "createdAt": "2026-05-01T12:34:56Z",
  "tags": ["narrator"],
  "samplePath": "sample.wav"
}
```

## Consequences
### Positive
- Editable by hand; trivial backup/sync (zip `voices\` or put it under OneDrive).
- No new binary dependency (SQLite avoided).
- Mirrors WhisperHeim's path conventions — code reuse is straightforward.
- Per-voice folder isolates a corrupt / partial entry.

### Negative
- JSON catalog rewrites are non-atomic by default; need write-temp-then-rename to avoid torn writes. Cheap to do.
- At 200+ voices the library.json scan/load grows; revisit then.

### Neutral
- `engine` field on profile leaves room for the multi-engine future without committing to one.

## Alternatives considered
- **SQLite catalog** — rejected for v1: overkill for 20–50 entries, adds a binary dep, fights the "edit by hand" affordance. Easy to migrate to later if needed.
- **Files only, no index** — rejected: scanning N folders + parsing N meta.json files on every UI open is wasteful and racy with capture flows.
- **Single combined file (one safetensors + one big JSON)** — rejected: fights the natural "delete one voice = delete one folder" gesture.

## References
- WhisperHeim DataPathService: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Services\Settings\DataPathService.cs`
- Vision: `.agenthoff/vision.md`
- Kyutai research: `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`
```

## Outcome

Decision recorded as ADR 0005 (`scope: global`, `status: accepted`). Voice profiles persist as one folder per voice under `<dataPath>\voices\<voice-id>\` (containing `profile.safetensors`, `meta.json`, optional `sample.wav`), indexed by a single `library.json` at `<dataPath>\voices\library.json`. Path layering matches WhisperHeim: bootstrap + settings in `%APPDATA%\Mockingbird` (with optional remapped `<dataPath>` for OneDrive sync), runtime + model weights in `%LOCALAPPDATA%\Mockingbird` (machine-local, never synced). `meta.json` schema fixed: `id`, `name`, `engine`, `pocketTtsVersion`, `source` (`mic|loopback|import`), `createdAt` (ISO-8601 UTC), `tags`, optional `samplePath`. Atomic writes via write-temp-then-rename. Migration to SQLite deferred until ~200+ voices. No code changed — implementation lands in main-009 (walking skeleton) and main-010 (voice CRUD).

Key files:
- `.agenthoff/knowledge/decisions/0005-voice-persistence-layout.md`
