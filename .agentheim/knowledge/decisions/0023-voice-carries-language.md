---
id: 0023
title: Voice profile carries its language; speak request body unchanged
scope: main
status: accepted
date: 2026-05-18
supersedes: []
superseded_by: []
related_tasks: [main-035, main-040, main-041]
related_research: [pocket-tts-german-support-2026-05-18, kyutai-tts-2026-05-01]
---

# ADR 0023: Voice profile carries its language; speak request body unchanged

## Context
pocket-tts 2.0.0+ ships six languages from the single `kyutai/pocket-tts`
HuggingFace repo. Language is bound at **model-instance load time** via
`TTSModel.load_model(language=...)`; the generation methods
(`generate_audio`, `generate_audio_stream`) take **no language argument**
(see research `pocket-tts-german-support-2026-05-18` sections 3 and 7).
This makes pocket-tts unusual among the multilingual TTS systems we surveyed:
Chatterbox-multilingual and Coqui XTTS-v2 are per-call (a single resident
multilingual model serves all languages with a `language_id` field on each
request), while pocket-tts requires one resident `TTSModel` per language —
or a reload — to switch.

Utterheim wants to serve English and German concurrently from the tray app
(decided in the work tranche captured 2026-05-18; sidecar will keep both
models warm — see main-036 / main-039). The open design question is: **when
a `POST /speak` arrives, how does the sidecar know which resident model to
route it to?**

Constraints we're working under:

- ADR 0003 fixed the speak HTTP API at `{"text": ..., "voice": ...}`.
  Adding a request field is a breaking change to the Claude Code plugin and
  every other client.
- ADR 0005 fixed the voice library layout (folder per voice + `library.json`
  index, `meta.json` per voice). The schema is already extensible; adding a
  field is cheap.
- Built-in voices already imply a language by construction: `alba` / `marius`
  / etc. are English-trained, `juergen` is German-trained (pocket-tts 2.1.0
  issue #166), and Kyutai themselves recommend matching prompt language to
  model language to avoid English-accented German output.
- The user (Marco) has stated as the orchestrator that the speak HTTP
  contract should not change and that language should ride with the voice.

## Decision
**Each voice profile carries exactly one language attribute.** The speak
HTTP request body stays at `{"text": ..., "voice": ...}` — language is
inferred from the named voice by the sidecar, not sent on the wire.

Concretely:

- The voice library's `meta.json` schema (ADR 0005) gains a required
  `language` field. Allowed values for v1: `english`, `german`. The schema
  change and the back-fill of `juergen` as a built-in German voice are
  implemented in **main-040**.
- The Voices page gains a language picker on the cloning flow so the user
  declares the target language at clone time. Implemented in **main-041**.
- The sidecar maintains a `voice_id → language` map (sourced from
  `library.json`) and routes each `/speak` request to the resident `TTSModel`
  for that language. If the voice's declared language isn't one of the
  preloaded models, the sidecar returns an error rather than reloading.
- The speak HTTP API stays exactly as ADR 0003 specifies. No `language`
  field is added to the request body, and no SSML envelope is introduced.

## Consequences

### Positive
- **Zero churn on every speak client.** The Claude Code plugin, the
  WhisperHeim integration path, curl-based scripts, and any future client
  keep working unchanged. ADR 0003's contract is preserved.
- **One source of truth per voice.** Re-cloning a voice for a new language
  is an explicit, visible act — it produces a new entry in the library, not
  a silent retag. This matches Kyutai's own guidance that voice prompts
  should be language-matched, and matches the built-in shape (one default
  voice per language).
- **Cheap to implement.** The library already has per-voice metadata
  (ADR 0005); adding one field plus a UI picker is a small change. The
  routing lookup on the sidecar is a dict access. Contrast with adding a
  request-level language field: every caller would need updating and the
  sidecar would still need to decide what to do when caller language and
  voice language conflict.
- **Natural fit for the per-instance binding constraint.** Because
  pocket-tts forces routing-to-a-resident-model anyway, the routing key has
  to live somewhere. The voice profile is the most discoverable place to
  keep it (it's already what the client names in the request).

### Negative
- **Voice library schema migrates.** `meta.json` v1 files lack `language`.
  Backfill rule defined in main-040: existing entries default to `english`
  (since v1 shipped English-only); the bootstrap reconciliation already
  exists (ADR 0011) so the on-disk migration is a one-pass rewrite.
- **No "speak this German text in alba's voice" without re-cloning.** If
  the user wants a single voice across languages they must clone twice (once
  per language). The Kyutai documentation is silent on cross-language
  `.safetensors` portability (research section 4, open question 1) and
  recommends language-matched prompts, so the loss is small but real.
- **Future "translate then speak" flows can't share a voice across
  languages** as a single entity. They'd have to look up the matching
  language sibling of the same speaker, or pick a language-appropriate
  fallback. Out of scope for v1 — flagged here for whoever does that
  feature.

### Neutral
- The four-options taxonomy below is now closed: re-opening it requires a
  superseding ADR, not a worker-time decision.
- The sidecar's existing `TypeError` fallback around `language=`
  (`PythonSidecar/utterheim_sidecar/main.py`) is dead code on pocket-tts
  2.0.0+ but unrelated to this ADR. Its removal is tracked separately in
  main-043.

## Alternatives considered
Per the German-support research's section 7, four interfaces were viable.
All four were considered and three were rejected.

1. **`language` field on the speak request body** —
   `{"text": "Guten Tag", "voice": "juergen", "language": "de"}`. Voice and
   language as independent fields; sidecar maps `de`→`german` and routes.
   **Rejected** because it breaks ADR 0003's contract (every client must
   change), and because it opens a conflict mode (voice declared for German
   but request says English) that has no good answer — the sidecar would
   either ignore the request field (making it a lie), error out (poor UX),
   or accept it and produce English-accented German (the exact failure mode
   `juergen` was added to prevent in pocket-tts 2.1.0, issue #166).
2. **Voice-profile-carries-language** — selected. See Decision above.
3. **Auto-detect from text** — sidecar runs `langdetect` / `lingua-py` on
   each request. **Rejected**: adds a new Python dependency for a problem
   we don't have (the calling agent always knows the language up-front),
   introduces non-determinism on short or code-mixed inputs, and still has
   to map the detected language to a resident model — so it doesn't even
   remove the routing step it claims to simplify. Suitable for systems with
   a single multilingual model (ElevenLabs); not suitable for pocket-tts's
   per-instance binding.
4. **SSML `xml:lang`** — wrap each request in an SSML envelope with a
   per-element language attribute. **Rejected**: overkill for a personal
   tray app, requires the Claude plugin to grow an SSML builder, and
   pocket-tts doesn't natively consume SSML so the sidecar would parse it
   only to extract one field. The enterprise Azure / Google pattern earns
   its weight on systems doing prosody control and multi-voice narration;
   utterheim is doing neither in v1.

Why **per-instance binding makes voice-carries-language uniquely cheap
for us**: in a per-call multilingual system (Chatterbox-multilingual,
XTTS-v2), voice-carries-language would still require the API to *also*
accept a per-call language for the legitimate "narrate this English quote
in the middle of a German paragraph in Voice X" case — making the voice
field a default rather than the sole source of truth. pocket-tts can't do
that case anyway (one model = one language for the whole utterance), so
the voice field can carry language without ambiguity loss.

## References
- ADR 0003 — Claude transport (speak HTTP API). This ADR explicitly does
  **not** change the request body shape defined there.
- ADR 0005 — Voice persistence layout. `meta.json` gains the `language`
  field in main-040; the folder layout, `library.json` index, and
  write-temp-then-rename semantics are unchanged.
- ADR 0011 — Bootstrap state reconciliation. Handles back-fill of
  `language` on pre-existing voice entries on first startup after upgrade.
- Research: `pocket-tts-german-support-2026-05-18` — section 3
  (per-instance binding), section 4 (cloning for German), section 7 (the
  four-options taxonomy this ADR closes), open question 1 (cross-language
  `.safetensors` portability).
- Research: `kyutai-tts-2026-05-01` — original pocket-tts evaluation that
  preceded the multilingual release.
- main-040 — voice library `language` field + `juergen` built-in.
- main-041 — Voices page language picker.
- main-043 — sidecar drops the now-dead `TypeError` fallback around
  `language=`.
