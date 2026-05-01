---
id: 0005
title: Voice profiles as folder-per-voice + library.json index
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-005, main-009, main-010]
related_research: [kyutai-tts-2026-05-01]
---

# ADR 0005: Voice profiles as folder-per-voice + library.json index

## Context
Voice profiles are `.safetensors` kvcache files plus metadata (name, source, dates, sample reference, engine tag). Vision target: 5–10 voices in v1, growing to 20–50. WhisperHeim's `DataPathService` already establishes a layered path convention (bootstrap in `%APPDATA%`, optional remapped data dir for OneDrive sync, runtime/models in `%LOCALAPPDATA%`). Settings persistence in WhisperHeim is plain JSON.

## Decision
- One folder per voice under `<dataPath>\voices\<voice-id>\` containing `profile.safetensors`, `meta.json`, and optionally `sample.wav`.
- A single `library.json` at `<dataPath>\voices\library.json` holding the master index (id → display name, engine, source, created, tags). Loaded on startup, reconciled against actual folder contents (orphan folders / missing entries surface a tray warning, never silently dropped).
- Settings live in `<dataPath>\settings.json` (synced) and bootstrap `%APPDATA%\Mockingbird\bootstrap.json` (machine-local pointer to the data path). Same pattern as WhisperHeim.
- Pocket-tts model weights and the bundled Python runtime live in `%LOCALAPPDATA%\Mockingbird\` (machine-local, not synced).

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

Field semantics:
- `id` — stable folder name; lowercase kebab; never renamed once published.
- `name` — user-facing display label; freely editable.
- `engine` — engine tag; v1 always `pocket-tts`, reserved for future multi-engine.
- `pocketTtsVersion` — version of the engine that produced the profile (compat checks).
- `source` — one of `mic` | `loopback` | `import`.
- `createdAt` — ISO-8601 UTC.
- `tags` — free-form labels for UI grouping/search.
- `samplePath` — optional, relative to the voice folder (typically `sample.wav`).

Atomic writes: both `library.json` and per-voice `meta.json` use write-temp-then-rename to avoid torn writes.

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
- Migration path to SQLite is straightforward if voice count or query patterns demand it later.

## Alternatives considered
- **SQLite catalog** — rejected for v1: overkill for 20–50 entries, adds a binary dep, fights the "edit by hand" affordance. Easy to migrate to later if needed.
- **Files only, no index** — rejected: scanning N folders + parsing N meta.json files on every UI open is wasteful and racy with capture flows.
- **Single combined file (one safetensors + one big JSON)** — rejected: fights the natural "delete one voice = delete one folder" gesture.

## References
- WhisperHeim DataPathService: `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Services\Settings\DataPathService.cs`
- Vision: `.agenthoff/vision.md`
- Kyutai research: `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`
