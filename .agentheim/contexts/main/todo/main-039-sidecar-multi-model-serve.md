---
id: main-039
title: Sidecar — load English + German models concurrently and route by voice's language
status: todo
type: feature
context: main
created: 2026-05-18
completed:
commit:
depends_on: [main-036, main-037, main-040]
blocks: []
tags: [multilingual, sidecar, python, runtime]
related_adrs: [0002, 0007, 0023, 0024, 0025]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-002, main-011, main-015]
---

## Why
ADR for `main-036` decides the sidecar preloads English + German concurrently;
ADR for `main-035` decides each voice carries its own language. The piece
that makes those two decisions actually work is the sidecar: it must hold
multiple `TTSModel` instances resident and route each request to the right
one based on the voice's language.

Today, the sidecar's `serve` command loads exactly one `TTSModel` and assigns
it to the module-level `pocket_tts.main.tts_model` slot that
`generate_data_with_state` reads. Multi-model means a routing layer in front
of that slot, or a different generate path that picks the right instance per
request.

## What
Modify `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` so the `serve`
command:

1. Accepts a list of languages (CLI flag, e.g. `--language english --language
   german`, or `--languages english,german`) and an optional default. If only
   one language is given, behaviour is unchanged from today (back-compat with
   ADR 0008 cross-cutting).
2. Loads one `TTSModel` per language at startup using
   `TTSModel.load_model(language=...)`.
3. Holds them in a `dict[str, TTSModel]` keyed by language name.
4. On each `/speak` request, looks at the request's `voice` field, resolves
   it through the C# host's voice library to find the voice's language (or:
   accepts a `language` field if the host chooses to send it derived from
   the voice — implementation choice for the worker; the wire contract is
   the host's, not the sidecar's).
5. Routes the request to the matching resident model and streams the audio
   back as today.

The C# host side (`PocketTtsEngine`, voice library service) needs the matching
change: when issuing a `/speak`, include the voice's language so the sidecar
can pick the right model. This task includes both ends.

## Acceptance criteria
- [ ] `utterheim_sidecar/main.py` `serve` command accepts multiple languages
      and loads one `TTSModel` per language at startup. CLI signature stays
      back-compat with the single-`--language` form.
- [ ] The sidecar's request handler routes to the correct resident model
      based on the request payload's language indicator (field name and shape
      chosen by the worker, but must be documented in this task's Notes when
      done).
- [ ] The C# `PocketTtsEngine` sends the voice's language with each speak
      request, sourced from the voice library entry (relies on `main-040`).
- [ ] Startup log records each loaded model and total load time, so the
      bootstrap diagnostic remains useful.
- [ ] A request for a voice whose language has no resident model returns a
      structured error (status code + message naming the missing language),
      not a process crash.
- [ ] Manual smoke: speak an English voice and a German voice back-to-back
      from Claude-Code; both work without sidecar restart.
- [ ] The redundant `TypeError` fallback around `language=` (see `main-043`)
      stays in this PR or is removed in `main-043` — the worker decides the
      coupling and notes it.

## Notes
The research's section 6 confirms the symbols the sidecar imports
(`web_app`, `tts_model`, `generate_data_with_state`, `export_model_state`)
all survive into pocket-tts 2.1.0 unchanged. The architectural lift is on
*our* side, not on pocket-tts breakage.

Open implementation choices for the worker:

- One process holding multiple models, or one sidecar subprocess per
  language? The ADR for `main-036` allows either; the worker picks based on
  process-lifecycle simplicity. One process is simpler; fork-per-language
  isolates crashes. Default: one process (matches the existing single-process
  pattern from ADR 0002 + 0012 JobObject).
- Routing field name: `voice.language` baked into the request, or
  `model_key` chosen by the host? Document the chosen field in this task's
  Notes when done.

Dependency on `main-040` is real: until the voice library knows each voice's
language, the host can't send it to the sidecar. If `main-040` ships first,
`main-039` can do the sidecar side and the C# side together. If the worker
prefers to land them as one PR, that's also fine — bundle is allowed.
