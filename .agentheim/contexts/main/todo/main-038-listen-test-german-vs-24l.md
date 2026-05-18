---
id: main-038
title: Listen-test german vs german_24l (spike)
status: todo
type: spike
context: main
created: 2026-05-18
completed:
commit:
depends_on: []
blocks: []
tags: [multilingual, spike, audit, model-selection]
related_adrs: [0025]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: []
---

## Why
ADR for `main-037` selects the distilled `german` over the undistilled
`german_24l` based on the rule "match English's variant". Kyutai themselves
flag the distilled models as the most likely source of quality regressions
and explicitly invite bug reports comparing the two. Before any user-facing
UI commits to a single German voice, we want one short empirical pass to
confirm that distilled German is *good enough* — not formally benchmarked,
just listened to.

If the distilled version has an obvious failure mode (clipping, weird stress,
robotic prosody on common German phrases) that 24l doesn't, the ADR for
`main-037` gets revisited.

## What
A small recorded comparison the user can listen to:

1. Generate the same German text on both `german` and `german_24l` using the
   pocket-tts CLI directly (no sidecar code change needed):
   ```
   pocket-tts generate --language german     --voice juergen --text "<sample>" -o de.wav
   pocket-tts generate --language german_24l --voice juergen --text "<sample>" -o de_24l.wav
   ```
2. Run the same comparison with at least one cloned voice — export a voice
   on each language with `pocket-tts export-voice --language german[_24l]`
   from the same source WAV, then generate.
3. Write a short note: filenames, what the user heard, verdict (distilled
   acceptable / distilled has problem X / 24l materially better).

Sample text suggestions (the worker picks one or two — small variety helps):

- A two-sentence "Nordwind und Sonne" excerpt (phonetically balanced; classic
  German diction test passage).
- A couple of typical Claude-Code notification strings ("Build erfolgreich.
  Tests laufen.").
- A long compound noun ("Donaudampfschifffahrtsgesellschaftskapitän") to
  stress-test segmentation.

## Acceptance criteria
- [ ] At least two WAV pairs (one with `juergen`, one with a cloned voice)
      generated against both `german` and `german_24l`.
- [ ] A short markdown note added to
      `.agentheim/knowledge/research/german-listen-test-2026-MM-DD.md` with
      the sample text used, the generated filenames, and the user's verdict.
- [ ] The research note's `related_tasks` lists `main-037` and `main-038`.
- [ ] If verdict is "distilled has problem X / 24l materially better" — a
      new backlog task is captured to revisit ADR for `main-037`; otherwise
      ADR `main-037` gets a one-line "confirmed by `main-038`" addendum.

## Notes
This is deliberately a low-fidelity spike, not a MOS evaluation. The point is
"does the user, listening, feel the distilled German is good enough for their
own daily use?" — that's the entire bar.

Output WAV files can live anywhere ephemeral; they don't need to be checked
in. Only the markdown note (and a one-line ADR addendum if no follow-up
task is needed) is the durable artifact.
