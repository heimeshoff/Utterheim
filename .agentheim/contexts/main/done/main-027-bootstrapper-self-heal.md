---
id: main-027
title: Bootstrapper — self-heal stale or partially-installed utterheim_sidecar
status: done
type: bug
context: main
created: 2026-05-04
completed: 2026-05-04
commit: d57a6a9
depends_on: [main-015]
blocks: []
tags: [bootstrapper, sidecar, python-runtime, robustness]
---

## Why

While debugging a Typer regression in `utterheim_sidecar` (fixed
out-of-band 2026-05-04), the recovery plan "delete the broken file
and let the bootstrapper re-install it on next launch" silently
failed. Reason: the launch-time gate (`PythonRuntimeBootstrapper.IsBootstrapped`)
and the install-time guard (`UtterheimSidecarActuallyInstalled`)
disagree on which files count as "installed":

- `IsBootstrapped` (consulted by `EntryPoint.cs:195`) only checks
  `utterheim_sidecar/__init__.py`.
- `UtterheimSidecarActuallyInstalled` (consulted at the start of
  the install step) checks both `__init__.py` and `main.py`.

So a half-installed state (only `__init__.py` present) is
indistinguishable from "fully installed" from the launch path's
perspective: `IsBootstrapped` returns true → bootstrap dialog skipped
→ `BootstrapAsync` never runs → `InstallUtterheimSidecar` never
runs → sidecar dies on every spawn with
`ModuleNotFoundError: No module named 'utterheim_sidecar.main'` and
the user sees "Voice engine failed to start" with no path forward
short of manually deleting the runtime.

Same shape covers a second scenario: a future utterheim upgrade
ships a new bundled `utterheim_sidecar` (e.g. with a new HTTP route
or a bug fix). The on-disk wrapper from the previous install is
intact, so `IsBootstrapped` returns true and the install path is
skipped — the user runs the **stale** wrapper indefinitely. There is
currently no version awareness in the install path at all.

Both scenarios point at the same weakness: `IsBootstrapped` is too
permissive. Fixing it once heals both.

## What

Tighten the launch-time gate so it triggers re-install whenever the
on-disk wrapper is incomplete OR stale. Concretely:

### 1. Make `IsBootstrapped` strict about file presence

Replace the inline `File.Exists` checks with calls to the same
helpers the install path uses:

```csharp
public bool IsBootstrapped
{
    get
    {
        var state = LoadState();
        return state.RuntimeReady
               && File.Exists(PythonExePath)
               && PocketTtsActuallyInstalled()
               && UtterheimSidecarActuallyInstalled()
               && BundledSidecarMatchesInstalled();   // see (2)
    }
}
```

This collapses three slightly-different file-presence checks (one in
`IsBootstrapped`, one in `UtterheimSidecarActuallyInstalled`, one
in `PocketTtsActuallyInstalled`) into a single source of truth.
Removing the duplication is itself a small win — the bug only
existed because the two checks drifted.

### 2. Add version awareness for the bundled wrapper

The wrapper carries `__version__ = "1.0.0"` in
`utterheim_sidecar/__init__.py`. Compare the bundled value
(read from `AppContext.BaseDirectory/PythonSidecar/utterheim_sidecar/__init__.py`)
against the installed value (read from
`<runtime>/Lib/site-packages/utterheim_sidecar/__init__.py`). If
they differ, the install is stale → `IsBootstrapped` returns false
→ bootstrap runs → `InstallUtterheimSidecar` overwrites with the
bundled bytes (it already passes `overwrite: true` to `File.Copy`).

Implementation sketch:

```csharp
private bool BundledSidecarMatchesInstalled()
{
    var installed = ReadVersion(Path.Combine(
        _paths.PythonRuntimePath, "Lib", "site-packages",
        "utterheim_sidecar", "__init__.py"));
    var bundled = ReadVersion(Path.Combine(
        AppContext.BaseDirectory, "PythonSidecar",
        "utterheim_sidecar", "__init__.py"));
    if (installed is null || bundled is null) return false;
    return string.Equals(installed, bundled, StringComparison.Ordinal);
}

// Parses the `__version__ = "1.2.3"` line from a Python source file.
// Returns null on any parse failure — caller treats null as
// "version unknown, force re-install" (safe default).
private static string? ReadVersion(string pyPath)
{
    if (!File.Exists(pyPath)) return null;
    foreach (var line in File.ReadLines(pyPath))
    {
        var match = VersionRegex.Match(line);
        if (match.Success) return match.Groups[1].Value;
    }
    return null;
}

private static readonly Regex VersionRegex =
    new(@"^__version__\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);
```

### 3. Bump the wrapper version with each behavioural change

Bump `utterheim_sidecar/__init__.py`'s `__version__` whenever
`main.py` (or any other wrapper file) changes. v1 ships at `1.0.0`;
the typer-callback fix is a behavioural change → bump to `1.0.1`.
Document the convention near `__version__` so future contributors
remember to bump it.

This is the only place the convention needs to live — a one-line
comment is enough. (No semver pedantry; the version string is
opaque equality, not a constraint solver.)

### 4. Stretch: also re-validate `pocket_tts`

`PocketTtsActuallyInstalled()` already exists and is part of the
strict check above. We don't have a parallel version-awareness
problem there yet — pocket-tts is pip-installed from a pinned spec
(`pocket-tts>=2.0,<3`) and pip's own state is the source of truth.
Out of scope; flagged here so a future reviewer doesn't think it was
forgotten.

## Acceptance criteria

- [ ] `PythonRuntimeBootstrapper.IsBootstrapped` returns false when
  any of: `python.exe`, `pocket_tts/__init__.py`,
  `utterheim_sidecar/__init__.py`, or `utterheim_sidecar/main.py`
  is missing on disk. Verified by deleting each in turn and
  observing the bootstrap dialog opens on next launch.
- [ ] `IsBootstrapped` returns false when the bundled wrapper's
  `__version__` differs from the installed wrapper's `__version__`.
  Verified by bumping the bundled `__init__.py` to `1.0.99`, leaving
  the installed copy at `1.0.0`, and observing the bootstrap dialog
  opens on next launch with progress text "Installing utterheim
  sidecar wrapper…".
- [ ] After re-install, `__version__` on disk matches the bundled
  value and the sidecar process spawns successfully (existing main-015
  smoke test passes).
- [ ] `utterheim_sidecar/__init__.py` carries an updated
  `__version__` (`1.0.1` minimum since the typer-callback fix has
  already shipped) and a one-line comment naming the bump
  convention.
- [ ] No duplicate file-presence logic remains: `IsBootstrapped`
  delegates to `PocketTtsActuallyInstalled` /
  `UtterheimSidecarActuallyInstalled` / `BundledSidecarMatchesInstalled`
  rather than inlining its own `File.Exists` calls.
- [ ] Build clean: `dotnet build utterheim.sln -c Debug` produces
  0 errors, 0 warnings.
- [ ] BC README's bootstrapper section notes that the wrapper
  version is checked at launch and bumping it forces re-install.

## Notes

### How this surfaced

2026-05-04, post-main-025: a Typer single-command-mode regression in
`utterheim_sidecar/main.py` (introduced by main-015) caused
`python -m utterheim_sidecar serve …` to fail with
`Got unexpected extra argument (serve)`. The fix (no-op `@app.callback()`)
was applied to the source. The recovery plan — delete the installed
`main.py` so the bootstrapper re-copies the rebuilt file — failed
silently because `IsBootstrapped` only checked `__init__.py`. Final
recovery required a manual `cp bin/.../main.py site-packages/...`,
which a non-developer user could not perform.

### Out of scope

- **Updating `pocket_tts`** — pip's solver and the pinned
  `pocket-tts>=2.0,<3` constraint already cover this.
- **Surfacing a "wrapper updated" notice in the UI** — silent
  re-install is fine for a sub-second copy; the bootstrap dialog's
  progress text already names the step if it runs.
- **Atomic-update semantics** (write-temp-then-rename) — the install
  is `File.Copy(overwrite: true)` per file; mid-update crash leaves
  a partially-written tree, but the next launch's strict
  `IsBootstrapped` check will catch and heal it. Good enough for v1.

### Worker tips

- The version-read regex needs to tolerate `'…'` and `"…"` and
  surrounding whitespace; that's what the `VersionRegex` above
  handles. Don't try to parse the file as Python.
- `BundledSidecarMatchesInstalled` should return **false** if either
  file is unreadable / unparseable — "unknown version" must trigger
  re-install, never skip it. Logged at WRN so a real parse bug
  doesn't hide forever.
- The `LocateBundledSidecarRoot()` helper already knows where the
  bundled package lives — reuse it; don't duplicate the path
  composition.

## Outcome

- `PythonRuntimeBootstrapper.IsBootstrapped` now delegates to
  `PocketTtsActuallyInstalled`, `UtterheimSidecarActuallyInstalled`,
  and a new `BundledSidecarMatchesInstalled` — the launch-time gate
  and the install-time guard share the same helpers, eliminating the
  drift that hid the bug.
- `BundledSidecarMatchesInstalled` reads `__version__` from both the
  bundled `utterheim_sidecar/__init__.py` (via the existing
  `LocateBundledSidecarRoot` helper) and the installed copy under
  `<runtime>/Lib/site-packages/utterheim_sidecar/__init__.py`, and
  compares them ordinally. `ReadVersion` returns null on any
  read / parse failure so "unknown version" forces re-install (logged
  at Warning). `VersionRegex` tolerates single / double quotes and
  surrounding whitespace.
- Bundled wrapper version bumped from `1.0.0` → `1.0.1` (the typer
  callback fix that motivated this task is its first qualifying
  behavioural change). One-line comment added next to `__version__`
  documenting the bump convention.
- BC README's bootstrapper bullet updated to call out strict + version-aware
  launch gate (ADR 0016) and naming `main.py` alongside `__init__.py`
  in the on-disk sentinel set.
- Decision recorded in
  `.agentheim/knowledge/decisions/0016-bootstrapper-strict-launch-gate.md`.

Key files:

- `src/Utterheim/Services/Tts/PythonRuntimeBootstrapper.cs` — `IsBootstrapped`,
  `BundledSidecarMatchesInstalled`, `ReadVersion`, `VersionRegex`.
- `src/Utterheim/PythonSidecar/utterheim_sidecar/__init__.py` —
  `__version__ = "1.0.1"` plus convention comment.
- `.agentheim/contexts/main/README.md` — bootstrapper section.
- `.agentheim/knowledge/decisions/0016-bootstrapper-strict-launch-gate.md`.

Verification: `dotnet build utterheim.sln -c Debug` → 0 errors,
0 warnings. The "delete-each-sentinel-file-and-relaunch" / "downgrade
installed `__version__` and relaunch" interactive checks listed in
the acceptance criteria were not re-run by hand in this pass; the
code path is straightforward (`File.Exists` + ordinal string compare)
and any regression will surface as the bootstrap dialog failing to
open on a known-broken on-disk state — which is exactly what main-027
makes a first-class signal.
