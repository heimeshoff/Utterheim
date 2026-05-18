---
id: main-035
title: Voice profile carries its language (decision)
status: done
type: decision
context: main
created: 2026-05-18
completed: 2026-05-18
commit:
depends_on: []
blocks: [main-040, main-041]
tags: [multilingual, voice-library, http-api, adr]
related_adrs: [0003, 0005, 0023]
related_research: [pocket-tts-german-support-2026-05-18, kyutai-tts-2026-05-01]
prior_art: [main-005, main-015]
---

## Why
pocket-tts binds language at `TTSModel.load_model(language=...)` time, not per
generate call. To support German and English in the same tray app we have to
choose how a speak request signals which resident model should serve it. The
user has decided that the voice profile is the carrier of language: every
voice (built-in or cloned) has exactly one language attribute, and the speak
HTTP API stays unchanged — `{"text": ..., "voice": ...}`. Language is inferred
from the named voice, not sent on the request.

This is decision (1) and (2) from the German-support research's open
questions section.

## What
Write an ADR documenting the voice-carries-language decision. The ADR captures:

- The runtime constraint that forced the choice (per-instance language binding
  in pocket-tts).
- The four design options considered (per-request language field,
  voice-carries-language, auto-detect-from-text, SSML).
- The selected option (voice-carries-language) and the rationale (simplest
  client surface; matches how built-in voices already imply a language; no new
  request fields; no plugin-side language detection).
- The consequences (voice library schema must gain a language column; cloning
  flow must ask the user which language; orthogonal future "translate then
  speak" features can't share a voice across languages without re-cloning).
- The rejected options and why.

## Acceptance criteria
- [ ] ADR file at `.agentheim/knowledge/decisions/00NN-voice-carries-language.md`
      (next free number) with frontmatter `status: accepted`, `scope: main`,
      `related_tasks: [main-035, main-040, main-041]`.
- [ ] ADR explicitly lists the four candidate interfaces (request-field,
      voice-carries-language, auto-detect, SSML) and the rejection rationale
      for each non-selected option.
- [ ] ADR records the consequence "voice library schema gains a `language`
      field" with a link to `main-040`.
- [ ] ADR records the consequence "speak request body is unchanged from the
      single-language design (ADR 0003)" with a link back to `0003`.
- [ ] `related_adrs` updated on this task file and on `main-040` / `main-041`
      after the ADR number is known.

## Notes
The research report's section 7 lays out the four options and notes that
pocket-tts is unique among the multilingual TTS systems surveyed in being
per-instance bound — Chatterbox-multilingual and XTTS-v2 are per-call.
That asymmetry is part of why voice-carries-language is cheaper here than it
would be for those systems: the routing decision has to happen somewhere
anyway, and the voice is the most natural place to keep it.

This task's output is the ADR itself — no code change. The worker writes the
ADR file, updates the indexes, and is done. The implementing features
(main-040, main-041) cite this ADR.

## Outcome
ADR 0023 written at `.agentheim/knowledge/decisions/0023-voice-carries-language.md`.

Decision: voice profile carries its language; the speak HTTP API
(`{"text": ..., "voice": ...}`) is unchanged from ADR 0003. The sidecar
routes each `/speak` request to the resident `TTSModel` for the language
declared on the named voice. `meta.json` (ADR 0005) gains a required
`language` field, implemented in main-040; the Voices page gains a
language picker on the cloning flow in main-041.

All four candidate interfaces (per-request `language` field,
voice-carries-language, auto-detect-from-text, SSML) are explicitly
enumerated in the ADR with rejection rationale for the three not chosen.
The ADR also records why the per-instance binding constraint in pocket-tts
makes voice-carries-language uniquely cheap here, in contrast to per-call
multilingual systems (Chatterbox-multilingual, XTTS-v2).
