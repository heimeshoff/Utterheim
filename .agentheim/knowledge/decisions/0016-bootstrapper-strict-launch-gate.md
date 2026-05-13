# 0016 — Bootstrapper launch-time gate is strict and version-aware

Date: 2026-05-04
Status: Accepted
Context: main BC, `PythonRuntimeBootstrapper`
Supersedes: nothing (extends ADR 0011)
Related: main-027, ADR 0011 (bootstrap-state reconciliation),
ADR 0015 (utterheim sidecar wrapper)

## Context

The bootstrapper has two file-presence checks that gate work:

- `IsBootstrapped` — consulted by `EntryPoint.cs` on every launch. If true, the
  bootstrap dialog is skipped and the host comes up assuming the runtime is
  ready.
- `UtterheimSidecarActuallyInstalled` / `PocketTtsActuallyInstalled` —
  consulted at the start of each install step in `BootstrapAsync`, defending
  against a stale `bootstrap-state.json` that outlived a wiped runtime
  (ADR 0011 / main-021).

These two layers checked different files. `IsBootstrapped` only asserted
`utterheim_sidecar/__init__.py`; the install-time guard also asserted
`utterheim_sidecar/main.py`. A half-installed state with `__init__.py`
present but `main.py` missing therefore looked "installed" to the launch path
and "not installed" to the install path, but the install path was never
reached because the launch path short-circuited first. The user saw
"Voice engine failed to start" with no path forward short of manually
deleting the runtime.

The same shape covers a second scenario: a future utterheim upgrade ships
a new wrapper. The on-disk wrapper from the previous install is intact, so
`IsBootstrapped` returns true and the install path is skipped — the user
runs the **stale** wrapper indefinitely.

## Decision

The launch-time gate is the **strictest** of all gates and delegates to the
same helpers the install path uses. `IsBootstrapped` is the composition of:

1. `state.RuntimeReady` (last-step smoke test passed at least once).
2. `File.Exists(PythonExePath)`.
3. `PocketTtsActuallyInstalled()` — `pocket_tts/__init__.py` is on disk.
4. `UtterheimSidecarActuallyInstalled()` — `utterheim_sidecar/__init__.py`
   AND `main.py` are both on disk.
5. `BundledSidecarMatchesInstalled()` — the bundled wrapper's `__version__`
   equals the installed wrapper's `__version__`.

If any of these is false, `IsBootstrapped` returns false → bootstrap dialog
opens → install step re-runs → `File.Copy(overwrite: true)` heals the
on-disk tree.

### Why opaque-equality version strings instead of semver

The wrapper carries `__version__ = "1.0.1"` in its `__init__.py`. The
bootstrapper compares strings ordinally — no semver pedantry, no version
constraint solver, no migration tables. The only question being asked is
"are these the same bytes?", and a string compare answers it cheaply.

Future wrapper changes:

- Bump the version string with each behavioural change.
- The mismatch triggers a silent re-install (sub-second copy operation;
  the bootstrap dialog's existing progress text "Installing utterheim
  sidecar wrapper…" names the step if it runs).
- No migration code, ever — the install step is "copy bundled bytes over
  installed bytes", and that's the entire migration story.

Tradeoff accepted: a version downgrade also triggers a re-install (since
the strings differ). That's correct — downgrades aren't supported, but if
they happened, re-installing the older bundled bytes is the right thing.

### Why "unknown version" forces re-install

`ReadVersion` returns null on any failure (file missing, IO error, no
version line, unreadable). `BundledSidecarMatchesInstalled` then returns
false. This is the safe default: if we can't read either side's version,
re-install to a known state. The mismatch path logs at Warning so a real
parse bug surfaces in the file log rather than hiding behind a perpetual
re-install loop.

## Consequences

Positive:

- Half-installed states (any wrapper file missing) heal on next launch.
- Wrapper updates ship by bumping `__version__`; the next launch re-installs.
- Single source of truth for "is this installed" — no more drift between
  inline `File.Exists` checks and helper methods.
- The install step's existing `File.Copy(overwrite: true)` is the migration
  primitive for free; no separate "upgrade" code path.

Negative:

- Future contributors must remember to bump `__version__` on wrapper
  changes. Mitigated by a one-line comment next to `__version__` naming the
  convention; reviewers will catch oversights when behavioural changes
  ship without a bump.
- Every launch reads two small Python files (`__init__.py` × 2) to
  extract `__version__`. Cost is negligible (~kilobytes, line-by-line
  read until the regex matches the first `__version__` line).

Not addressed (out of scope):

- `pocket_tts` version drift — pip's solver and the pinned
  `pocket-tts>=2.0,<3` constraint already cover this; the bundled
  wrapper's version is the only thing we own end-to-end.
- Atomic-update semantics (write-temp-then-rename per file) — the install
  is `File.Copy(overwrite: true)`; mid-update crash leaves a partially
  written tree, but the next launch's strict `IsBootstrapped` catches and
  heals it.
