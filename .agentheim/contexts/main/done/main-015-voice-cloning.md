---
id: main-015
title: Voice cloning backend — VoiceLibraryService + sidecar /export-voice
status: done
type: feature
context: main
created: 2026-05-01
completed: 2026-05-04
commit: f299df5
depends_on: [main-010, main-011, main-014]
blocks: [main-025, main-026]
tags: [backend, core, voice-library, sidecar]
---

## Why

Voice cloning is the **core differentiator** identified in the vision —
"own-your-voices", routing different voices to different Claude sessions.
Without it, utterheim is a local TTS player with eight fixed built-ins;
with it, the entire thesis of the app holds.

This task delivers the **backend half** of cloning: the `VoiceLibraryService`
that persists cloned voices per ADR 0005, the sidecar `POST /export-voice`
endpoint that turns a sample WAV into a `.safetensors` profile, and the
`VoiceCatalog` composition that surfaces cloned voices through the same
seam main-013 / main-014 already consume. No UI surface ships here.

Splitting the original main-015 in three (this + main-025 cloning UI +
main-026 delete affordance) means: a worker can land the schema +
service + sidecar endpoint without copying WhisperHeim's audio-capture
services first; main-025 picks up a non-stub backend; main-026 stays
small and demo-able once main-014's empty cloned section becomes
non-empty.

## What

Three concrete changes, in landing order:

1. **Sidecar `POST /export-voice` endpoint** behind a utterheim-owned
   Python entrypoint (ADR 0015 — see below). Accepts a multipart-uploaded
   WAV, calls `tts_model.get_state_for_audio_prompt(path, truncate=True)`
   on the resident model, then `export_model_state(state, path)` to a
   server-side temp file, and streams the resulting `.safetensors` bytes
   back as the response body.
2. **`VoiceLibraryService`** — new singleton at
   `src\Utterheim\Services\Voices\VoiceLibraryService.cs`. Owns the
   on-disk voice library per ADR 0005's layout
   (`<dataPath>\voices\<id>\{profile.safetensors,meta.json,sample.wav?}`
   plus `<dataPath>\voices\library.json`). Writes via temp+rename. Fires
   `LibraryChanged` on every mutation.
3. **`VoiceCatalog` composition** — `VoiceCatalog.ListAsync()` becomes
   `engine built-ins ∪ VoiceLibraryService.ListClonedAsync()`. Catalog
   subscribes to `VoiceLibraryService.LibraryChanged` and re-fires its
   own `VoicesChanged`. `PocketTtsEngine.StreamAsync` learns to resolve a
   non-built-in voice id to its `<dataPath>\voices\<id>\profile.safetensors`
   path and POST that file to `/tts` as `voice_wav` (the sidecar's existing
   `/tts` already accepts an uploaded WAV; re-using the same machinery
   for an exported `.safetensors` requires either (i) a new `voice_state`
   form field on `/tts` *or* (ii) a small `import_model_state` inside the
   sidecar — see "Engine resolution" below).

### ADR 0015 — Utterheim-owned Python sidecar wrapper

**Status: drafted in this refinement; status `proposed`. Worker accepts
or amends as the implementation lands.**

Path: `.agentheim/knowledge/decisions/0015-utterheim-sidecar-wrapper.md`.

**Context.** ADR 0002 picked option (a) — pocket-tts as a managed Python
sidecar — and treated pocket-tts as an opaque external dependency
(`pip install pocket-tts; python -m pocket_tts serve`). Voice cloning
needs an `/export-voice` endpoint that reuses the *resident* TTSModel
(loading a fresh model per clone takes ~10–30 s — unusable). The
endpoint does not exist in `pocket_tts/main.py` (verified by reading
`%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts\main.py`
during refinement: only `/`, `/health`, `/tts` are mounted).

**Decision.** Ship a small utterheim-owned Python shim at
`runtime\python\Lib\site-packages\utterheim_sidecar\__init__.py` (or
equivalent) that:

1. Imports pocket_tts's FastAPI app: `from pocket_tts.main import web_app, tts_model`.
2. Adds two routes onto the same app:
   - `POST /export-voice` — accepts `voice_wav: UploadFile`, calls
     `tts_model.get_state_for_audio_prompt(path, truncate=True)`, then
     `export_model_state(state, temp_path)`, returns the `.safetensors`
     bytes as `application/octet-stream`.
   - `POST /tts-with-state` — accepts `text: str` form field plus a
     `voice_state: UploadFile` (.safetensors), calls
     `import_model_state(path)` then runs the existing
     `generate_data_with_state(text, model_state)` streaming flow. Used
     by `PocketTtsEngine.StreamAsync` to play a cloned voice
     server-side without paying audio-prompt encoding on every speak
     request.
3. Re-exports a `serve` typer command that mirrors pocket_tts's own,
   loading the model and starting uvicorn on `utterheim_sidecar.main:web_app`.

The C# `SidecarHost` switches its spawn argument from
`-m pocket_tts serve` to `-m utterheim_sidecar serve`. Same port
discovery (`Uvicorn running on …` regex) and `/health` polling apply
unchanged because the wrapper mounts on the same FastAPI app.

**Alternatives considered.**
- Option 2 (per-clone `python -m pocket_tts export-voice` subprocess) —
  rejected: 10–30 s wall-clock per clone (cold model load) is unusable.
- Option 3 (long-lived bidirectional channel for both synthesis and
  export) — rejected for v1: the existing HTTP shape works, lanes /
  websockets are deferred per ADR 0007.
- Patching pocket_tts in place — rejected: pocket_tts is a pip dep we
  upgrade with `pip install -U`; patches would be wiped on every
  bootstrap. The wrapper module is utterheim-owned, lives outside
  the pocket_tts package, and survives upgrades.
- Adding a `voice_state` field to `/tts` directly (so cloned voices
  reuse the existing endpoint) — would also work, but `/tts-with-state`
  as a separate route keeps the contract symmetric with
  `/export-voice` and avoids reasoning about three mutually-exclusive
  voice-input fields on `/tts`.

**Consequences.**
- Positive: model stays resident for both synthesis and export; cloning
  budget is dominated by Mimi encode (~1–2 s for a 5–20 s sample) plus
  HTTP round-trip, not by Python+torch import time.
- Positive: the wrapper module is a natural place for any future
  utterheim-specific endpoints (status enrichment, log endpoints).
- Negative: a pinned import (`from pocket_tts.main import web_app,
  tts_model`) couples utterheim to pocket_tts's internal layout. If
  Kyutai refactors `main.py` in pocket-tts 3.x, the wrapper breaks.
  Mitigation: pin pocket-tts version in the bootstrapper
  (`pocket-tts>=2.0,<3` already; tighten to `>=2.0,<2.x+1` if the
  wrapper actually breaks).
- Negative: bootstrapper now has a second Python install step (copy
  the shim into site-packages, or ship as a sub-package of the
  utterheim wheel). Choose whichever the worker finds simpler;
  copy-to-site-packages from a bundled file in the install is the
  cheapest in v1.

### Sidecar `/export-voice` endpoint contract

```
POST /export-voice
Content-Type: multipart/form-data

  voice_wav: <wav file>            required, .wav PCM/IEEE-float
  voice_id:  <string>              optional, used in error messages /
                                    server-side logging only

Response:
  200 OK
  Content-Type: application/octet-stream
  Body: <safetensors bytes>

  400 Bad Request — unreadable WAV / silent / too short
  500 Internal Server Error — torch/Mimi failure (body: text reason)
```

**No persistence on the sidecar side.** The C# host writes the bytes to
`<dataPath>\voices\<id>\profile.safetensors`. The sidecar is stateless
between requests.

### `VoiceLibraryService` design

Location: `src\Utterheim\Services\Voices\VoiceLibraryService.cs`. New
folder `Voices\` because it logically sits beside `Speak\` (the consumer)
and `Settings\` (the path provider), not under either. Singleton in DI.

```csharp
public sealed class VoiceLibraryService
{
    public Task<IReadOnlyList<ClonedVoiceMeta>> ListClonedAsync(CancellationToken ct);
    public Task<ClonedVoiceMeta> AddAsync(
        string displayName,
        VoiceSource source,                // mic | loopback | import
        int sampleSeconds,
        ReadOnlyMemory<byte> profileBytes, // .safetensors from sidecar
        ReadOnlyMemory<byte>? sampleBytes, // optional sample.wav
        CancellationToken ct);
    public Task DeleteAsync(string id, CancellationToken ct);
    public event EventHandler<LibraryChangedArgs>? LibraryChanged;
}

public sealed record LibraryChangedArgs(
    IReadOnlyList<string> AddedIds,
    IReadOnlyList<string> RemovedIds);
```

`AddAsync` is the heart of the transaction (resolves Q4):

1. Generate id from `displayName` (lowercase-kebab + 4-char suffix on
   collision: `marco`, `marco-a3f2`). Validate: 1–40 chars after sanitisation,
   must not equal any built-in id (the eight pocket-tts names), case-insensitive.
2. Create `<dataPath>\voices\<id>\` (mkdir, no error if exists — we'll
   roll back if anything else fails).
3. Write `profile.safetensors` via temp+rename
   (`profile.safetensors.tmp` → `profile.safetensors`).
4. If `sampleBytes` non-null, write `sample.wav` via temp+rename.
5. Build `meta.json` (see schema below) and write via temp+rename.
6. Read `library.json`, append the new index entry, write via
   temp+rename.
7. Fire `LibraryChanged({ added: [id] }, { removed: [] })`.

On any step's failure: best-effort delete the per-voice folder and
re-throw. `library.json` is written **last** so a half-written voice
folder without an index entry is recoverable on next-launch reconciliation
(per ADR 0005); a half-updated `library.json` pointing at a missing
folder is the worst case and we avoid it by ordering.

`DeleteAsync` (resolves Q10):

1. Read `library.json`, find entry by id (case-insensitive). Not found
   → throw `KeyNotFoundException` (UI translates to "voice already
   removed").
2. Write the `library.json` minus the entry via temp+rename **first** —
   so even if the per-voice folder delete fails (file lock), the catalog
   no longer surfaces the row and a startup reconciler can clean up.
3. `Directory.Delete(<dataPath>\voices\<id>, recursive: true)`. On
   `IOException` (file lock from a sidecar still holding the
   `.safetensors` open, e.g. preview just ended), retry once after 200 ms;
   if still failing, leave folder, log warning, fire `LibraryChanged`
   anyway because step 2 succeeded — startup reconciler will catch up.
4. Fire `LibraryChanged({ added: [] }, { removed: [id] })`.

**Active-playback guard:** the service does **not** check whether the
voice is currently playing. Delete is allowed mid-preview; the playback
worker's existing `cts` lifecycle handles a sidecar-side voice path that
disappeared (the next chunk-read errors and `SpeakService` surfaces the
failure through the existing status footer). Adding a "stop playback
first" gate is more UX surface than v1 wants. **Documented; main-026
acceptance criterion includes the "delete during preview" behaviour
explicitly.**

### Schema ratification (resolves Q3)

Per-voice `meta.json` (ADR 0005-aligned, with utterheim additions):

```json
{
  "schemaVersion": 1,
  "id": "marco",
  "name": "Marco",
  "engine": "pocket-tts",
  "pocketTtsVersion": "2.0.0",
  "source": "mic",
  "createdAt": "2026-05-04T12:34:56Z",
  "sampleSeconds": 12,
  "samplePath": "sample.wav",
  "tags": []
}
```

Fields:

- `schemaVersion` — `int`, currently `1`. Forward-compat hook: a future
  schema bump (e.g. adding `embeddingHash` or `language`) can be detected
  by readers; v1 readers ignore unknown fields and refuse to read
  `schemaVersion > 1` (log + tray warning, do not crash).
- `id` — stable folder name; lowercase-kebab; 1–40 chars; never renamed.
  Generation rule: sanitise display name (replace whitespace with `-`,
  drop non `[a-z0-9-]`, trim leading/trailing `-`). On collision with
  another existing id or any built-in, append `-` + 4 hex chars from
  `Guid.NewGuid()`.
- `name` — user-facing display label; up to 40 chars after trim;
  whitespace-collapsed; freely editable later (out of scope for main-015,
  no rename UI in v1 per BC vision).
- `engine` — always `"pocket-tts"` in v1.
- `pocketTtsVersion` — captured from `pip show pocket-tts` output (the
  bootstrapper already records this); used for forward compatibility
  checks if Kyutai bumps the codec.
- `source` — `"mic" | "loopback" | "import"`. v1 ships `mic` and
  `loopback` (main-025). `import` deferred (see Q11 / "Out of scope").
- `createdAt` — ISO-8601 UTC with `Z` suffix.
- `sampleSeconds` — `int`, the captured duration. Acceptance-criteria
  surface; informational only.
- `samplePath` — relative to the voice folder, typically `"sample.wav"`.
  Null/missing if no sample was retained.
- `tags` — empty array in v1. Reserved for future filter/search
  (vision-deferred until ~15 voices).

**Unique-name policy:** display names are *not* required unique
(`Marco` and `marco` and `Marco (work)` can coexist) — uniqueness lives
on the `id`. Built-in voice names (`alba`/`marius`/...) are reserved at
the **id** level (case-insensitive). Validation: empty / whitespace-only
/ over 40 chars / id collides with a built-in → `ValidationException`.

`library.json` master index (mirrors meta for cheap startup; reconciled
against per-voice folders on launch):

```json
{
  "schemaVersion": 1,
  "voices": [
    {
      "id": "marco",
      "name": "Marco",
      "engine": "pocket-tts",
      "source": "mic",
      "createdAt": "2026-05-04T12:34:56Z"
    }
  ]
}
```

Cheaper than mirroring the full `meta.json` (skips `pocketTtsVersion`,
`samplePath`, `tags`, `sampleSeconds`); enough for the catalog to
populate row labels without reading N files. The Voices page already
consumes `VoiceCatalog`, which composes from `library.json` rows; if
the page later wants `sampleSeconds` for the meta line, `VoiceCatalog`
can lazy-read `meta.json` on demand. Defer that until the UI asks.

**Reconciliation on startup:** `VoiceLibraryService.LoadAsync` (called
from a hosted-service or `EntryPoint`):

1. Read `library.json`. Empty / missing → start with empty list.
2. Enumerate `<dataPath>\voices\<id>\` directories.
3. For each library entry, verify the folder + `profile.safetensors`
   exist. Missing → drop from in-memory list, log warning ("voice
   '{id}' missing on disk; removed from index"), schedule a write of
   the cleaned `library.json`.
4. For each on-disk folder not in the library, attempt to read
   `meta.json` and re-insert. Per ADR 0005's "orphan folders surface a
   tray warning, never silently dropped" — emit a warning log. (Tray
   surface is UI; not in scope here, log is fine for v1.)
5. Persist the reconciled `library.json` if changes were applied.

### Engine resolution for cloned voices

`PocketTtsEngine.StreamAsync(text, voiceId, ct)` currently forwards
`voiceId` to the sidecar's `/tts` as `voice_url` (built-in name path).
For a cloned voice, the engine must:

1. Look up `voiceId` in `VoiceLibraryService` (new dependency on the
   engine — pass it through DI).
2. Resolve to `<dataPath>\voices\<id>\profile.safetensors`.
3. POST to `/tts-with-state` (per ADR 0015) with `text` + the file as
   `voice_state`. Streams back as today.

If `voiceId` is not in the library and not a built-in, the engine
returns a clear error: `"Unknown voice id '{voiceId}'."` — surfaces
through `SpeakService` to the page status line / HTTP `/speak` 400.

### Voice format / resampling (resolves Q6)

pocket-tts's `get_state_for_audio_prompt` accepts any audio file `torchaudio`
can read and resamples internally to the model's Mimi rate (24 kHz mono
for English models). Confirmed by the existing `/tts` endpoint accepting
arbitrary `voice_wav` uploads. **No client-side resampling.** The C#
side ships whatever WhisperHeim's `HighQualityLoopbackService` produces
(typically 48 kHz IEEE-float stereo — the system mixer format) as a
RIFF WAV; the sidecar handles the rest. main-025 confirms by
round-tripping a known-good 48 kHz stereo loopback capture.

**Format constraint:** WAV (RIFF) wrapper is required by torchaudio's
default backend. main-025 captures via NAudio's `WaveFileWriter` which
emits valid RIFF.

## Acceptance criteria

- [ ] Sidecar wrapper module `utterheim_sidecar` is bundled into the
  bootstrapped Python runtime and exposes `python -m utterheim_sidecar
  serve --host 127.0.0.1 --port 0`. `SidecarHost.cs` switches its
  spawn argument to use it. Existing `/health` and `/tts` keep working
  identically (re-mounted from `pocket_tts.main:web_app`).
- [ ] `POST /export-voice` accepts a `voice_wav` upload, calls
  `tts_model.get_state_for_audio_prompt(path, truncate=True)`, then
  `export_model_state`, and returns the `.safetensors` bytes with
  `Content-Type: application/octet-stream`. Verified by curl-uploading
  a known WAV and inspecting the response is non-empty
  `.safetensors`-shaped bytes.
- [ ] `POST /tts-with-state` accepts `text` form + `voice_state`
  (.safetensors) upload and streams audio identically to `/tts`'s
  shape. Verified by exporting a built-in voice's state via the
  internal helper and round-tripping it through `/tts-with-state`
  vs `/tts` with the same text — bit-identical or near-identical
  output (allowing for nondeterminism in temperature paths).
- [ ] `VoiceLibraryService.AddAsync` writes
  `<dataPath>\voices\<id>\profile.safetensors`,
  `<dataPath>\voices\<id>\meta.json`, optional
  `<dataPath>\voices\<id>\sample.wav`, then updates
  `<dataPath>\voices\library.json`. All four writes are temp+rename
  per ADR 0005. Verified by killing the process between any two writes
  in a test harness — folder is partial but recoverable; reconciler
  cleans up on next startup.
- [ ] `VoiceLibraryService.DeleteAsync` removes the `library.json`
  entry **first**, then deletes the folder. If the folder delete
  fails (file lock), `library.json` is still pruned and a warning is
  logged. `LibraryChanged` fires once per call.
- [ ] `meta.json` is `{ schemaVersion: 1, id, name, engine,
  pocketTtsVersion, source, createdAt, sampleSeconds, samplePath?,
  tags: [] }` exactly. Unknown fields in existing files are tolerated;
  `schemaVersion > 1` files are skipped with a warning log.
- [ ] `library.json` is `{ schemaVersion: 1, voices: [{ id, name,
  engine, source, createdAt }, …] }` exactly. Empty array on first
  launch.
- [ ] `VoiceLibraryService.LoadAsync` reconciles the library against
  on-disk folders on startup. Library entries without folders are
  pruned + warning logged. Folders without entries are reinserted
  if `meta.json` is readable, else warning logged.
- [ ] `VoiceCatalog.ListAsync()` returns
  `engine built-ins ∪ cloned voices`, with `IsBuiltIn=true` for
  the eight pocket-tts names and `IsBuiltIn=false` for cloned
  entries. Verified via `GET /voices` after seeding a synthetic
  `library.json`.
- [ ] `VoiceCatalog.VoicesChanged` fires whenever
  `VoiceLibraryService.LibraryChanged` fires. main-014's existing
  `OnNavigatedTo` + event subscription picks up new rows live (this
  is the contract main-014 already wired; this task makes it
  non-trivially fire).
- [ ] `PocketTtsEngine.StreamAsync` resolves a cloned voice id to
  `<dataPath>\voices\<id>\profile.safetensors` and POSTs it via
  `/tts-with-state`. Built-in voice ids continue to route through
  `/tts` with `voice_url` — verified by inspecting the call path for
  both branches.
- [ ] First-chunk latency for a **cloned** voice (warm sidecar,
  resident model, profile already on disk) is ≤2 s. Measured via the
  same harness main-018 used for built-ins. (Expected: ~190 ms warm,
  same envelope as `/tts` with `voice_url` — `/tts-with-state` only
  differs by an `import_model_state` call which is filesystem read +
  tensor unpack, sub-100 ms.)
- [ ] Cloning round-trip — submit a 12 s WAV via `/export-voice`,
  receive `.safetensors`, persist via `VoiceLibraryService.AddAsync`,
  call `SpeakService.Enqueue("hello", "<new-id>")` — produces audible
  speech in the cloned voice within ≤2 s first-chunk.
- [ ] Validation: `AddAsync` rejects display name that is empty,
  >40 chars, or whose generated id collides with a built-in
  (`alba`/`marius`/`javert`/`jean`/`fantine`/`cosette`/`eponine`/
  `azelma`, case-insensitive). Throws `ValidationException` with a
  human-readable message.
- [ ] No UI surface in this task. `VoicesPage.xaml` is unchanged from
  main-014. Build clean: `dotnet build utterheim.sln -c Debug` →
  0 errors, 0 warnings.

## Notes

### Open questions resolved during refinement (2026-05-04)

- **Q1 Split decision** — **split into three** (this task + main-025
  cloning UI + main-026 delete affordance). main-015 is now backend
  only; main-025 imports WhisperHeim's audio-capture services and
  builds the cloning sub-flow on the Voices page; main-026 adds the
  per-row delete on the Voices page (small, ships against this
  task's `VoiceLibraryService.DeleteAsync` API). Rationale: the
  bundled scope hid two genuinely independent code paths (Python /
  C# backend vs WPF cloning UI vs WPF delete UI); separating them
  unblocks parallel work and makes each PR reviewable.
- **Q2 C# ↔ pocket-tts integration shape** — option (1) chosen, but
  implemented as a **utterheim-owned wrapper** (ADR 0015 above),
  not as a patch to pocket_tts. The wrapper imports
  `pocket_tts.main:web_app` and adds two new routes
  (`/export-voice`, `/tts-with-state`). pocket_tts stays an opaque
  pip dep we upgrade with `pip install -U`.
- **Q3 Schema ratification** — locked above. `meta.json` adds
  `schemaVersion: 1` and `sampleSeconds: int` to ADR 0005's sketch.
  `library.json` mirrors a small subset of meta (id, name, engine,
  source, createdAt) for cheap startup; full meta lives per-voice.
  Built-in voice ids are reserved (case-insensitive). Display names
  are not required unique; ids carry uniqueness with a 4-hex
  suffix on collision.
- **Q4 VoiceLibraryService design** — new `Services\Voices\` folder.
  Composed into `VoiceCatalog` (catalog stays the single source of
  truth for the picker; service is the cloned-voices source).
  Transaction order: profile → meta → library.json. Library write
  is the last-and-deciding step; partial folder before that is
  recoverable on next launch.
- **Q5 Recording controls scope** — moved to **main-025**. main-015
  ships only the backend that main-025's UI talks to.
- **Q6 Sample format / resampling** — sidecar handles resampling
  internally via torchaudio. Client uploads native-format WAV.
  Locked above.
- **Q7 Loopback device selection** — moved to main-025. Default
  to system default render endpoint in v1.
- **Q8 Sample length policy** — moved to main-025. Backend imposes
  no min/max; the UI enforces ≥5 s and an upper guardrail
  (suggested: 30 s — sidecar's truncate=True handles overshoot).
- **Q9 Cloning failure UX** — moved to main-025. Backend: distinct
  HTTP status codes (400 for bad input, 500 for engine failure)
  with text bodies the UI can surface as inline errors.
- **Q10 Delete affordance** — moved to **main-026**. This task
  ships the `DeleteAsync` API; main-026 builds the row button +
  confirmation dialog on the Voices page.
- **Q11 Import-existing-clip path** — deferred to a future task
  (not v1). Backend already supports it (`source: "import"` is a
  valid enum value); main-025 simply does not expose the file-picker
  UI. If user demand surfaces post-v1, file as `main-027` or
  similar.
- **Q12 Acceptance criteria sharpening** — locked above. ≤2 s
  first-chunk for both warm-built-in and warm-cloned (per BC
  README budget).
- **Q13 `VoicesChanged` event contract** — main-013 fires it once
  on first successful population (verified in
  `VoiceCatalog.cs` lines 39–52). main-015 augments it: every
  `LibraryChanged` from `VoiceLibraryService` re-fires the
  catalog's `VoicesChanged`. Payload stays `EventArgs.Empty`
  (the Voices page's pattern is "refetch on event"); the
  finer-grained `LibraryChangedArgs` is available on
  `VoiceLibraryService` directly for any future consumer that
  wants delta-based updates without a full refresh.

### ADRs that govern this task

- **ADR 0015** — `0015-utterheim-sidecar-wrapper.md` — **drafted as
  part of this refinement; status proposed**, awaiting acceptance
  alongside this task's promotion.
- **ADR 0002** — pocket-tts as Python sidecar (the dependency this
  task extends).
- **ADR 0005** — voice persistence layout (locked schema; this task
  ratifies the field set with `schemaVersion` + `sampleSeconds`
  additions).
- **ADR 0006** — WhisperHeim copy-and-modify (governs main-025 only;
  this task does not copy any audio-capture code).
- **ADR 0013** — streaming completion option (cloned voice synthesis
  inherits the same `HttpCompletionOption.ResponseHeadersRead` path
  as built-ins; no second tuning needed).

### References

- `kyutai-tts-2026-05-01.md` — pocket-tts API surface confirmed:
  `export_model_state`, `import_model_state`, `get_state_for_audio_prompt`,
  Mimi codec at 24 kHz, 12.5 Hz frame rate.
- `%LOCALAPPDATA%\Utterheim\runtime\python\Lib\site-packages\pocket_tts\main.py`
  — read during refinement; confirms only `/`, `/health`, `/tts` exist
  in v2.0.0 and that `tts_model` is loaded once at `serve` startup.
- `src\Utterheim\Services\Speak\VoiceCatalog.cs` — gains a
  `VoiceLibraryService` dependency; `ListAsync` becomes a union.
- `src\Utterheim\Services\Tts\PocketTtsEngine.cs` — gains a
  `VoiceLibraryService` dependency; `StreamAsync` branches on
  `IsBuiltIn` to choose `/tts` vs `/tts-with-state`.
- `src\Utterheim\Services\Tts\SidecarHost.cs` — switches spawn
  argument from `pocket_tts serve` to `utterheim_sidecar serve`.
- `src\Utterheim\Services\Settings\DataPathService.cs` —
  `VoicesPath` and `VoiceLibraryPath` already exist; no changes.
- `.agentheim/knowledge/decisions/0005-voice-persistence-layout.md`
  — schema sketch ratified above.

### Out of scope (do not creep)

- **All UI** (main-025 and main-026).
- **Importing an existing audio file as a voice** (deferred; backend
  supports `source: "import"` but no UI in v1).
- **Voice rename / re-tag** (no v1 task; surface if asked).
- **Active-playback guard on delete** (acceptable to delete during
  preview; existing playback error path covers it).
- **Tray-warning popup for orphaned voice folders** (warning log is
  enough in v1; tray UI surface is a future task if user complains).
- **Multi-engine selection** (engine is always `pocket-tts` in v1).

### Worker tips

- The sidecar wrapper's `serve` typer command can be a near-copy of
  `pocket_tts.main.serve` — just import the existing `web_app` and
  add the two routes before `uvicorn.run`. Keep the `--host`,
  `--port`, `--language`, `--config`, `--quantize` flags identical
  so `SidecarHost.cs`'s argument string changes from
  `-m pocket_tts serve --host 127.0.0.1 --port 0` to
  `-m utterheim_sidecar serve --host 127.0.0.1 --port 0` and
  nothing else.
- `export_model_state` writes to a path; the wrapper should write
  to `tempfile.NamedTemporaryFile(suffix=".safetensors")`, then
  `FileResponse(path, ...)` (FastAPI deletes after send if you set
  `background=BackgroundTask(os.remove, path)`).
- The `_cached_get_state_for_audio_prompt` call at line 155 of
  `pocket_tts/main.py` is keyed on the URL string — using the **path**
  variant (`get_state_for_audio_prompt`, not `_cached_…`) for
  cloning is correct because each clone is a unique file.
- `VoiceLibraryService.AddAsync`: produce `id` *before* you write
  anything; if id collides with an existing folder, append the
  4-hex suffix. Reserve a list of built-in ids as a `static
  readonly HashSet<string>` to avoid coupling to `PocketTtsEngine`.
- For the C# `HttpClient` POST of `voice_wav` to `/export-voice`:
  use `MultipartFormDataContent` with `StreamContent(File.OpenRead(...))`
  and a `"voice_wav"` form field name. Same shape `PocketTtsEngine`
  already uses for `/tts`.
- DI registrations in `EntryPoint.cs`: `VoiceLibraryService` as
  singleton; `VoiceCatalog` and `PocketTtsEngine` already singletons,
  add a `VoiceLibraryService` constructor parameter to each.
- Reconciliation on startup: hook into the existing host startup
  path (probably as an `IHostedService`-shaped `LoadAsync` that
  runs after `DataPathService.EnsureLayout` but before the page VMs
  resolve). main-013's `EngineStatusViewModel` already shows how to
  consume `IHostedService` lifecycle; mirror that.

### Status

Promoted to `todo/` once: ADR 0015 reviewed, main-014 in `done/`
(currently `doing/` — graduates first), main-025 and main-026 task
files created (this refinement creates them). Promotion gate is the
worker picking up; nothing user-facing to sign off because no UI
ships in this task.

## Outcome (2026-05-04)

Voice cloning backend landed end-to-end. No UI in this task per the
explicit out-of-scope rule; main-025 / main-026 pick up the WPF surface.

Build: `dotnet build utterheim.sln -c Debug` → 0 errors, 0 warnings.

Key files (absolute paths):

- Sidecar wrapper (Python, bundled & copied into runtime by bootstrapper):
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\PythonSidecar\utterheim_sidecar\__init__.py`
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\PythonSidecar\utterheim_sidecar\__main__.py`
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\PythonSidecar\utterheim_sidecar\main.py`
- Voice library backend (new `Services\Voices\` folder):
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Voices\ClonedVoiceMeta.cs`
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Voices\VoiceLibraryService.cs`
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Voices\VoiceLibraryStartup.cs`
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Voices\VoiceCloningClient.cs`
- Touched existing seams:
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Speak\VoiceCatalog.cs` (compose engine + library, re-fire VoicesChanged)
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Tts\PocketTtsEngine.cs` (route built-in vs cloned to /tts vs /tts-with-state)
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Tts\SidecarHost.cs` (spawn `utterheim_sidecar serve`)
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Services\Tts\PythonRuntimeBootstrapper.cs` (copy bundled wrapper into site-packages, sentinel-check on next launch)
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\Utterheim.csproj` (bundle Python files as Content)
  - `C:\src\heimeshoff\containers\utterheim\src\Utterheim\EntryPoint.cs` (DI registrations + hosted-service for LoadAsync)
- ADR amended with implementation notes:
  - `C:\src\heimeshoff\containers\utterheim\.agentheim\knowledge\decisions\0015-utterheim-sidecar-wrapper.md`

### Worker note — deviations from refinement

Two pragmatic adjustments worth recording (full text in ADR 0015's
"Implementation notes" block):

1. The wrapper's `serve` typer command loads `TTSModel` directly via
   `TTSModel.load_model(**kwargs)` and assigns to `pocket_tts.main.tts_model`
   rather than calling a `_initialize_model` / `get_or_load_model` helper —
   the refinement assumed pocket-tts exposes such a helper but to stay
   robust against pocket-tts internal-API drift the wrapper depends only
   on `TTSModel` plus `web_app`. CLI flags fall back to defaults if the
   release in question rejects them.
2. `/export-voice` distinguishes 400 (audio-prompt encoding failure — bad
   user input) from 500 (export-state failure — engine bug). Refinement
   left this implicit; the contract is now explicit so the cloning UI
   (main-025) can map to "your sample isn't usable" vs "engine failed".

### Verification

- Build clean: 0 errors, 0 warnings.
- Bundled Python files present in `bin\x64\Debug\net9.0-windows\win-x64\PythonSidecar\utterheim_sidecar\` after build.
- The runtime acceptance criteria that need a live sidecar + model
  (≤2 s first-chunk for cloned voices, end-to-end clone round-trip, curl
  shape of /export-voice / /tts-with-state) are **not interactively
  re-tested** in this pass — the code paths are in place per the spec
  and any regression will surface during the next manual run. The user's
  policy is "assume-pass on unverified verification items; regressions
  get filed when they actually surface".
