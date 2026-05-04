---
id: 0015
title: Mockingbird-owned Python sidecar wrapper for /export-voice
scope: global
status: accepted
date: 2026-05-04
supersedes: []
superseded_by: []
related_tasks: [main-015]
related_research: [kyutai-tts-2026-05-01]
---

# ADR 0015: Mockingbird-owned Python sidecar wrapper for /export-voice

## Context

Voice cloning (main-015 / main-025) needs to convert a captured WAV
sample into a `.safetensors` voice profile via pocket-tts'
`get_state_for_audio_prompt` + `export_model_state`. ADR 0002 picked
"pocket-tts as a managed Python sidecar over loopback HTTP" and
treated pocket-tts as opaque (`pip install pocket-tts; python -m
pocket_tts serve`).

Three integration shapes were on the table during main-015's
refinement:

1. Extend the running sidecar with a `POST /export-voice` endpoint —
   reuses the **resident** `TTSModel` instance.
2. Spawn `python -m pocket_tts export-voice <in> <out>` as a one-shot
   subprocess per clone. The CLI's `export_voice` command does
   `TTSModel.load_model(...)` which is a fresh ~10–30 s load on each
   call.
3. Long-lived bidirectional channel (websocket / unix pipe) carrying
   both synthesis and export traffic.

Reading
`%LOCALAPPDATA%\Mockingbird\runtime\python\Lib\site-packages\pocket_tts\main.py`
during refinement confirmed:

- pocket-tts 2.0.0's FastAPI app exposes only `/`, `/health`, `/tts`.
  No `/export-voice` route exists.
- `TTSModel` is loaded **once** as a module-level global in `serve` and
  reused across `/tts` calls.
- `export_model_state` is already imported at module top.
- The CLI `export_voice` command does its own `TTSModel.load_model(...)`
  — i.e. it does not share state with a running server.

Option (2) is unusable for v1 UX (10–30 s wall-clock per clone). Option
(3) is over-engineered for a request/response operation. Option (1) is
the right shape — but mockingbird does not own
`kyutai-labs/pocket-tts`. Patching `main.py` in-place fights `pip
install -U` and ADR 0002's "consume pocket-tts as canonical" stance.

## Decision

Ship a small **mockingbird-owned Python wrapper** module at
`runtime\python\Lib\site-packages\mockingbird_sidecar\` that:

1. Imports pocket-tts's existing FastAPI app and tts_model:
   `from pocket_tts.main import web_app, tts_model` (after running
   `pocket_tts.main.serve`-equivalent model load).
2. Mounts two new routes on the **same** `web_app`:
   - `POST /export-voice` — accepts `voice_wav: UploadFile`, calls
     `tts_model.get_state_for_audio_prompt(path, truncate=True)`,
     then `export_model_state(state, temp_safetensors_path)`,
     returns the `.safetensors` bytes as
     `application/octet-stream`. Cleans up temp files in a
     background task after the response is sent.
   - `POST /tts-with-state` — accepts `text: str` form + `voice_state:
     UploadFile` (.safetensors), calls `import_model_state(path)`,
     then runs the existing `generate_data_with_state(text,
     model_state)` streaming flow. Used by `PocketTtsEngine.StreamAsync`
     to play a cloned voice without paying audio-prompt encoding on
     every speak request.
3. Re-exports a `serve` typer command mirroring `pocket_tts.main.serve`
   (same `--host` / `--port` / `--language` / `--config` / `--quantize`
   flags) that loads the model and starts uvicorn against
   `mockingbird_sidecar.main:web_app`.

The C# `SidecarHost` switches its spawn argument from
`-m pocket_tts serve --host 127.0.0.1 --port 0` to
`-m mockingbird_sidecar serve --host 127.0.0.1 --port 0`. Same
port-discovery regex (`Uvicorn running on https?://[^:]+:(\d+)`) and
`/health` polling apply unchanged because the wrapper mounts on the
same FastAPI app and uvicorn logs the same banner.

**Bootstrapper change:** the runtime bootstrapper either copies the
`mockingbird_sidecar` package into site-packages from a bundled file
in the install, or pip-installs it from a wheel co-located with the
mockingbird release. The choice is ADR-incidental; copy-from-bundle
is the cheapest in v1 because the wrapper has no dependencies of its
own (`fastapi`, `typer`, `uvicorn`, `torch` all come transitively
from pocket-tts).

## Consequences

### Positive

- **Resident model is preserved.** Cloning latency is dominated by
  Mimi audio-prompt encoding (~1–2 s for a 5–20 s sample) plus HTTP
  round-trip — not by Python+torch import time.
- **Synthesis with cloned voices stays warm.** `/tts-with-state`'s
  `import_model_state` is a filesystem read + tensor unpack
  (sub-100 ms in practice) on top of the same streaming path
  `/tts` already uses. First-chunk-on-Preview for a cloned voice
  inherits ADR 0013's ≤2 s budget unchanged.
- **pocket-tts stays opaque.** ADR 0002's "consume pocket-tts as
  canonical, upgrade with `pip install -U`" intent is preserved
  — the wrapper sits **outside** the pocket_tts package.
- **Natural extension point.** Future mockingbird-specific endpoints
  (status enrichment, log endpoints, multi-engine adapter) have an
  obvious home.

### Negative

- **Pinned import to pocket_tts internals.** The wrapper does
  `from pocket_tts.main import web_app, tts_model`. If Kyutai
  refactors `main.py` in pocket-tts 3.x (renames `web_app`, moves
  `tts_model` out of module scope, splits the file), the wrapper
  breaks at import. Mitigation: pin pocket-tts to a tested major
  in the bootstrapper (already `pocket-tts>=2.0,<3`), and surface
  a clear error from the wrapper if the import fails ("This
  pocket-tts version is incompatible with mockingbird; update
  mockingbird or downgrade pocket-tts.").
- **Bootstrapper now ships a Python artefact mockingbird owns.**
  Distribution adds a step (copy the wrapper into the runtime).
  Acceptable cost; the artefact is small (one-or-two `.py` files
  totalling <200 lines).
- **Slight cognitive load** — readers of `main.py` won't see
  `/export-voice` defined there; they'll have to know the wrapper
  exists. Documented in the BC README's "Engine status" section
  and in this ADR.

### Neutral

- The wrapper module is a candidate for upstream contribution to
  pocket-tts later — `/export-voice` would be welcome there. For
  v1, owning it keeps mockingbird in control of the surface.
- The `tts_model` global in pocket-tts 2.x is a public-shaped
  attribute (lowercase, no underscore prefix). Treating it as
  stable is reasonable.

## Alternatives considered

- **Option 2 (per-clone CLI subprocess)** — rejected: 10–30 s
  wall-clock per clone makes cloning UX worse than dictating the text
  by hand. Disqualifying.
- **Option 3 (long-lived bidirectional channel)** — rejected for v1:
  pure overkill for a request/response operation. ADR 0007 already
  defers lanes / websocket-style transport to v1.5; if that lands,
  revisit.
- **Patch `pocket_tts/main.py` in place** — rejected: fights
  `pip install -U`; patches lost on every bootstrap; couples
  mockingbird to a specific pocket-tts release in a way that's
  invisible from outside the runtime. The wrapper module is
  mockingbird-owned, lives outside the pocket_tts package, and
  survives upgrades cleanly (subject to the import-pinning caveat
  above).
- **Add a `voice_state` form field to `/tts`** (so cloned voices
  reuse the existing endpoint) — workable, but `/tts-with-state`
  as a separate route keeps the contract symmetric with
  `/export-voice` (both are wrapper-owned), avoids reasoning about
  three mutually-exclusive voice-input fields on `/tts`
  (`voice_url`, `voice_wav`, `voice_state`), and keeps the surface
  analysable in isolation if a future engine swap lands.
- **A second sidecar process** dedicated to export — rejected: two
  resident TTSModels = double the RAM bill (~400 MB each at FP32).
  The export endpoint runs only on user action; sharing the
  synthesis sidecar is fine.

## References

- ADR 0002: pocket-tts as Python sidecar (the dependency this
  wrapper extends).
- ADR 0013: streaming completion option (latency budget cloned
  synthesis inherits via `/tts-with-state`).
- pocket-tts 2.0.0 source confirmed during refinement:
  `%LOCALAPPDATA%\Mockingbird\runtime\python\Lib\site-packages\pocket_tts\main.py`
  — `web_app`, `tts_model`, `generate_data_with_state` are
  module-level; `export_model_state` and `import_model_state`
  are imported from `pocket_tts.models.tts_model`.
- Kyutai research: `.agenthoff/knowledge/research/kyutai-tts-2026-05-01.md`.
- main-015: backend implementation (the wrapper's first consumer).

## Implementation notes (2026-05-04, main-015)

The wrapper landed roughly as drafted with two pragmatic adjustments worth
recording for future maintainers:

- **`serve` command loads the model itself, not via a `_initialize_model`
  helper.** The ADR draft assumed a `pocket_tts.main._initialize_model` (or
  `get_or_load_model`) helper would be available to invoke the model load
  preamble. To stay robust against pocket-tts internal-API drift we instead
  call `TTSModel.load_model(**kwargs)` directly inside
  `mockingbird_sidecar.main:serve` and assign the result to
  `pocket_tts.main.tts_model`. This makes the wrapper depend only on
  `from pocket_tts import TTSModel` plus `from pocket_tts.main import web_app`
  — both of which are stable across pocket-tts 2.x releases per the
  research doc. The CLI flags (`--language`, `--config`, `--quantize`)
  remain best-effort: if pocket-tts's `load_model` doesn't accept one of them
  in this release we log a warning and fall back to defaults so the sidecar
  still starts.
- **HTTP error code policy on `/export-voice`.** Failures inside
  `get_state_for_audio_prompt` (torchaudio refused the input, audio is
  silent / too short) are mapped to **400** with the underlying error message
  in the body. Failures inside `export_model_state` are **500** (engine bug,
  not user input). The C# `VoiceCloningClient` surfaces both as
  `InvalidOperationException` with a "/export-voice returned NNN: …"
  message; the cloning UI (main-025) translates 400 vs 500 to "your sample
  isn't usable" vs "engine failed".

