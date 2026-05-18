---
id: main-043
title: Drop dead `TypeError` fallback around `language=` in sidecar
status: done
type: chore
context: main
created: 2026-05-18
completed: 2026-05-18
commit:
depends_on: []
blocks: []
tags: [sidecar, cleanup, python]
related_adrs: []
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-002, main-011]
---

## Why
The utterheim sidecar (`src/Utterheim/PythonSidecar/utterheim_sidecar/main.py`,
roughly lines 280–322 per the research's section 6) has a defensive
`try / except TypeError` around `TTSModel.load_model(language=...)` that
predates pocket-tts 2.0.0 — back when `language=` wasn't a recognised kwarg
and would raise. Since pocket-tts 2.0.0 (2026-04-21) the kwarg is always
accepted, so the `except TypeError` branch is unreachable code with the
project's pinned version range.

It's harmless, but it's also misleading: a reader sees the fallback and
might infer that language support is conditional. With multi-language
becoming a deliberate feature (ADRs from `main-035` / `main-036`), the dead
branch is actively confusing.

## What
Remove the `try / except TypeError` wrapper around the `language=` kwarg
call in `utterheim_sidecar/main.py`. Replace with the straight call. Bump
the pocket-tts version pin in the sidecar's dependency manifest to `>=2.0.0`
(or `>=2.1.0` if anything in `main-039` uses 2.1-specific behaviour) so the
unconditional kwarg is contractual, not accidental.

## Acceptance criteria
- [ ] The `try / except TypeError` block around `language=` is gone from
      `utterheim_sidecar/main.py`.
- [ ] The pocket-tts dependency pin in the sidecar's manifest (the
      `pyproject.toml` / `requirements.txt` the bootstrapper from `main-011`
      uses) is set to `>=2.0.0` at minimum.
- [ ] Sidecar starts and serves a request end-to-end after the change (manual
      smoke or existing test).
- [ ] No new lint warnings introduced.

## Notes
This task is intentionally small and independent — no decision dependencies,
no UI surface. Can land alongside `main-039` (the sidecar multi-model work)
if the worker is in that file anyway, or as a standalone trivial PR.

If `main-039` lands first and already touches the same lines, this task
becomes a no-op and gets closed with a note pointing at the `main-039`
commit.

## Outcome

The dead `try / except TypeError` wrapper around `TTSModel.load_model(**load_kwargs)`
in `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` (the `serve` command,
formerly lines 311–322) is gone. The call is now a single unconditional
`model = TTSModel.load_model(**load_kwargs)`, with a comment pointing at the
contractual pin.

The pocket-tts pin in `src/Utterheim/Services/Tts/PythonRuntimeBootstrapper.cs`
(`PocketTtsSpec`) was tightened from `pocket-tts>=2.0,<3` to the explicit
`pocket-tts>=2.0.0,<3`, with the comment now noting that the lower bound is
contractual for the `language=` kwarg (German support landed in pocket-tts
2.0.0, 2026-04-21 — see the related research report).

Smoke verification: `python -c "import ast; ast.parse(...)"` on the edited
main.py reports `syntax ok`. The bootstrapper's own
`import pocket_tts; import utterheim_sidecar` smoke check (step 4, per
ADR 0015 / main-011) will exercise the real import path on next launch
inside the bootstrapped runtime.

Key files:
- `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` (dead block removed)
- `src/Utterheim/Services/Tts/PythonRuntimeBootstrapper.cs` (pin tightened)
