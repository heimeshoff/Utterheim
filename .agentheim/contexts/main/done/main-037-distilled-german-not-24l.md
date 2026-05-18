---
id: main-037
title: Production German is distilled `german`, not `german_24l` (decision)
status: done
type: decision
context: main
created: 2026-05-18
completed: 2026-05-18
commit:
depends_on: []
blocks: [main-039]
tags: [multilingual, model-selection, adr]
related_adrs: [0025]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: []
---

## Why
pocket-tts ships two German model lineages: the distilled 6-layer `german`
(default, real-time on CPU) and the undistilled 24-layer `german_24l`
("higher quality but slower", preview). Same split exists for fr / it / es /
pt. English has no `*_24l` variant — only the distilled `english`.

The user's rule: **match German's model size to English's.** Since English has
no 24l, German uses distilled. This keeps both languages on the same
performance profile (~6× real-time, ~200 ms first chunk) and avoids creating
a per-language UX inconsistency where one language feels noticeably slower.

This is decision (4) from the German-support research's open questions
section. Spike `main-038` validates the choice by ear before any UI commits
to it.

## What
Write a short ADR documenting the model-variant selection rule. The ADR
captures:

- The two German variants and the same-shape variants for other languages.
- The user's selection rule ("German matches English's lineage; English has
  no 24l, so German uses distilled").
- The contingency: if English ever gains a `english_24l` and the user adopts
  it, German switches to `german_24l` in lockstep — the ADR records this as
  a rule, not a one-time choice.
- The validation: `main-038` is the listen-test that confirms the distilled
  German is acceptable; if it's noticeably worse than 24l, revisit this ADR.
- The consequence: the sidecar config and voice library don't need a
  per-voice "model variant" axis. Voices are language-tagged; the language
  determines the model variant via the project rule.

## Acceptance criteria
- [ ] ADR file at `.agentheim/knowledge/decisions/00NN-german-distilled-default.md`
      (next free number) with frontmatter `status: accepted`, `scope: main`,
      `related_tasks: [main-037, main-038, main-039]`.
- [ ] ADR states the rule explicitly: "German model variant matches the
      English production variant; with `english` (distilled, no 24l), we use
      `german`."
- [ ] ADR lists the contingency for the future ("if English adopts a 24l
      variant, German follows").
- [ ] ADR references `main-038` as the empirical check that closes the loop
      on this decision.
- [ ] `related_adrs` on this task, `main-038`, and `main-039` updated after
      the ADR number is assigned.

## Notes
This decision is small but worth its own ADR because it codifies a *rule*
("match German to English"), not a point-in-time pick. Without the ADR, the
rule lives only in this task's `Why` section, which gets archived to `done/`.

`main-038` runs in parallel — the listen-test is the audit, not a
prerequisite for the ADR. If 038 turns up something surprising
(distilled-German has a specific failure mode 24l avoids), the ADR gets a
"Status: superseded by 00NN" addendum.

## Outcome

ADR 0025 (`german-distilled-default`) captures the project rule:
**German's model variant tracks English's production variant**; with
English on distilled `english` (no `*_24l` counterpart exists), German
loads the distilled `german` and not `german_24l`. The rule, not the
one-time pick, is the load-bearing artifact — if English ever adopts a
24l lineage, German switches in lockstep. Spike `main-038` is the
empirical listen-test that can supersede this ADR if distilled German
shows a failure mode 24l avoids. Direct consequence for the schema: no
per-voice "model variant" axis is needed — voice → language → variant
flows as data + rule.

Files:
- `.agentheim/knowledge/decisions/0025-german-distilled-default.md`
