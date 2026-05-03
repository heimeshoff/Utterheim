---
id: 0011
title: Bootstrap state — on-disk presence is authoritative, JSON flags are advisory
scope: main
status: accepted
date: 2026-05-03
supersedes: []
superseded_by: []
related_tasks: [main-011, main-021]
related_research: []
---

# ADR 0011: Bootstrap state reconciliation — on-disk presence is authoritative

## Context

`PythonRuntimeBootstrapper` persists `bootstrap-state.json` so a half-finished
first-run bootstrap survives restarts (per ADR 0008's "model bootstrap UX"
section). The state file lives in `%LOCALAPPDATA%\Mockingbird\` and tracks
four boolean flags: `PythonExtracted`, `PipInstalled`, `PocketTtsInstalled`,
`RuntimeReady`.

main-021 surfaced a subtle hazard: the state file and the runtime it describes
have **independent lifetimes**. A user (or a future installer, or a clean-machine
verification dance) can wipe `runtime\python\` while leaving the state JSON
intact. When that happens, every flag in the JSON is a lie.

The original implementation (main-011) handled this partially:

- Step 1 (extract Python) gated on `!state.PythonExtracted || !File.Exists(PythonExePath)`.
- Step 2 (install pip) gated on `!state.PipInstalled || !PipExists()`.
- Step 3 (install pocket-tts) gated on `!state.PocketTtsInstalled` **only**.
- `IsBootstrapped` correctly checked `pocket_tts/__init__.py` on disk before
  saying "you can skip the bootstrap dialog".

The asymmetry on step 3 is exactly what bit us: a stale `PocketTtsInstalled: true`
in JSON caused the bootstrapper to skip the install but still run the smoke test,
which then failed with an opaque "exit code 1".

## Decision

**On-disk presence is authoritative; the JSON flags are an optimisation hint.**

Every step of the bootstrap MUST gate on both:
1. Its persisted flag (so a half-finished run can resume), AND
2. A cheap on-disk existence check for a sentinel file the step is responsible for.

Sentinel files per step:
- Step 1 (Python extraction): `python.exe`
- Step 2 (pip install): `Scripts\pip.exe` or `Lib\site-packages\pip\__init__.py`
- Step 3 (pocket-tts install): `Lib\site-packages\pocket_tts\__init__.py`
- Step 4 (smoke test): not gated — always runs and produces `RuntimeReady`

Additionally, when an upstream step actually re-runs (i.e. its sentinel was
missing despite the flag being set), every downstream flag MUST be reset to
`false` before continuing. This makes the state machine **monotonic with
respect to on-disk reality**: the flags can only be `true` if everything
beneath them is also true on disk.

## Consequences

Positive:
- Manually wiping `runtime\` is a supported "force re-bootstrap" gesture.
- The bootstrap is robust against partial corruption (e.g. a single
  site-packages folder being deleted) without requiring the user to also
  delete `bootstrap-state.json`.
- The existing dialog behaviour (cancel, retry, resume from last good step)
  continues to work because the flags still mean "this step finished
  successfully on this disk" — just with a tighter definition of "this disk".

Negative:
- Slightly more work per `BootstrapAsync` call: four `File.Exists` probes
  on top of the JSON read. Negligible.
- Adding a new step requires picking a sentinel and following the pattern.
  Documented in code comments at each step.

## Alternatives considered

- **Drop the JSON entirely; recompute state from disk every launch.** Rejected:
  the state file is also useful for forensics ("which step did the previous
  run get to?") and re-deriving step 2's "we ran get-pip.py successfully"
  state purely from disk is fiddly. The current shape is clearer.
- **Verify state freshness via a runtime fingerprint (hash of `python.exe`
  embedded in the JSON).** Rejected: more code, more failure modes, doesn't
  improve the user-visible behaviour over the simpler "flag + on-disk
  sentinel" rule.

## Notes

- See `src/Mockingbird/Services/Tts/PythonRuntimeBootstrapper.cs` —
  `PocketTtsActuallyInstalled()`, the defensive flag resets at the top of
  steps 1 and 2, and the file-presence checks in each step's gate.
- Companion fix in main-021: subprocess stderr is now captured and replayed
  at `LogError` on non-zero exit (and embedded in the thrown exception
  message). This is what made the bug diagnosable when it happened — it's
  not architectural enough to warrant its own ADR but is documented in the
  source.
