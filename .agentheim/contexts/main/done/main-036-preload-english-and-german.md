---
id: main-036
title: Sidecar preloads English + German concurrently (decision)
status: done
type: decision
context: main
created: 2026-05-18
completed: 2026-05-18
commit: 3fddb55
depends_on: []
blocks: [main-039]
tags: [multilingual, sidecar, runtime, adr]
related_adrs: [0002, 0007, 0023, 0024]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-002, main-011]
---

## Why
Per the German-support research, `TTSModel.load_model(language=...)` binds
language for the model instance's lifetime; `generate_audio` takes no language
argument. To serve a German voice and an English voice without per-request
model reloads (which would defeat the warm-sidecar pattern), the sidecar must
hold both models resident at the same time.

The user has decided: preload both. RAM is not a concern on the user's
hardware, so the multi-model resident pattern wins over reload-on-change and
over single-default constraints.

This is decision (3) from the German-support research's open questions
section.

## What
Write an ADR documenting the multi-language preload decision. The ADR
captures:

- The pocket-tts constraint (per-instance language binding).
- The three options considered (preload list, reload-on-change,
  single-default).
- The selected option (preload list — start with English + German) and the
  rationale (warm performance for both languages; user's RAM headroom makes
  the ~270 MB cost acceptable; matches the voice-carries-language decision in
  ADR for main-035 by ensuring every voice's language has a live model
  available).
- The consequences (sidecar `serve` API must accept a list of languages;
  startup time grows by the load time of each extra model; adding a third
  language is a configuration change, not a code change).
- Open follow-ups: when a sixth language gets requested someday, revisit
  whether all preload at once still makes sense, or whether on-demand load
  becomes preferable.

## Acceptance criteria
- [ ] ADR file at `.agentheim/knowledge/decisions/00NN-sidecar-multi-language-preload.md`
      (next free number) with frontmatter `status: accepted`, `scope: main`,
      `related_tasks: [main-036, main-039]`.
- [ ] ADR explicitly enumerates the three candidate strategies (preload list,
      reload-on-change, single-default) and the rejection rationale for each
      non-selected option.
- [ ] ADR cites ADR 0002 (Python sidecar) and ADR 0007 (queue lives in C#
      host) as the structural context this builds on.
- [ ] ADR notes the relationship to ADR for main-035 (voice-carries-language)
      — together they make the routing story coherent: each voice declares a
      language, and the sidecar always has a resident model for it.
- [ ] `related_adrs` on this task and on `main-039` updated after the ADR
      number is assigned.

## Notes
The research's section 3 says ~135 MB per resident model on Linux/macOS
(single-source community measurement), so en+de ≈ 270 MB. The user explicitly
de-prioritised the Windows RAM-validation spike — they have enough headroom
that an empirical measurement isn't blocking. If RAM ever becomes a constraint
(e.g. on a leaner deployment target), this decision is reversible without
breaking the voice-carries-language contract.

This task's output is the ADR itself — no code change. The implementing
feature is `main-039`.

## Outcome

ADR 0024 (`sidecar-multi-language-preload`) accepted: the sidecar preloads
a fixed list of languages at startup (v1: English + German), holds one
`TTSModel` per language resident for the process lifetime, and routes each
`/speak` request via the voice's declared language (per ADR 0023) to the
matching resident model. Voices declaring an unpreloaded language are
rejected rather than loaded on demand, keeping the warm-sidecar guarantee
(ADR 0002) honest. The three candidate strategies from the German-support
research's open-questions section are documented with rejection rationale.
Sixth-language threshold flagged as the open follow-up that triggers a
revisit of preload-all-at-once.

Key files:
- `.agentheim/knowledge/decisions/0024-sidecar-multi-language-preload.md`
