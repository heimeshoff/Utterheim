---
id: main-047
title: Language-aware narration — detect EN/DE and speak in a matching voice
status: done
type: feature
context: main
created: 2026-05-24
completed: 2026-05-24
commit:
depends_on: []
blocks: []
tags: [integration, claude, plugin, narrator, language]
related_adrs: [0028, 0023, 0024, 0025, 0003]
related_research: [pocket-tts-german-support-2026-05-18]
prior_art: [main-019, main-035, main-039, main-040, main-041]
---

## Why

Utterheim now serves English and German concurrently (ADR 0024) and routes each
`POST /speak` to the right resident model by the named voice's declared language
(ADR 0023 — the wire body stays `{text, voice}`). But the Claude Code narrator
(the `utterheim-narrator` plugin, main-019) still resolves a single voice and is
language-blind: a German end-of-turn summary gets spoken by an English-tagged
voice, so the sidecar routes it to the English model and produces
English-accented German — the exact failure `juergen` was added to prevent.

We want the plugin to detect whether Claude's spoken text is German or English
and speak it in a language-matching voice, while preserving the vision's core
feature: each parallel Claude session still sounds distinct by ear. So each repo
configures a PAIR of voices — one English, one German — and the detected
language picks within that pair. No server change: the sidecar already does the
right thing once the client sends a language-matching `voice` id.

## What

Extend the existing `utterheim-narrator` plugin at `claude-code-plugin/` (NOT a
new artifact, NOT the older `examples/claude-hooks/` kit) with two pure,
unit-testable capabilities, then wire them in:

1. **Language detection** — a lightweight, dependency-free, offline PowerShell
   heuristic that classifies the already-resolved/markdown-stripped spoken text
   as `german` or `english`. No external library, no network call.

2. **Voice-pair resolution** — `utterheim-speak.ps1` resolves a voice for the
   DETECTED language using the two-slot config defined in ADR 0028:
   - English/default slot = legacy `./.claude/utterheim-voice` → `$env:UTTERHEIM_VOICE` → `alba`.
   - German slot = `./.claude/utterheim-voice-de` → `$env:UTTERHEIM_VOICE_DE` → fall back to the resolved English/default slot.
   - `-Voice` parameter, when supplied, overrides everything and skips detection.
   - `off`/`none`/`-` mute is evaluated on the FINALLY-resolved voice for the detected language.

3. **`/narrator` evolution** — the slash command sets/show both slots and uses
   the `language` attribute from `GET /voices` to validate each slot.

4. **Plugin version bump** — `plugin.json` `0.1.0` → next, as the consumer-update trigger.

### Detection heuristic spec (be this concrete)

- **Input:** the already-resolved, markdown-stripped spoken text (the `$summary`
  the Stop hook builds, or the notification `$message`). Detection runs on that
  final string, not the raw transcript.
- **Signals (German):**
  - **Umlaut/ß hit:** presence of any of `ä ö ü Ä Ö Ü ß`.
  - **Stopword hits:** count of whole-word, case-insensitive matches against a
    German stopword set. Minimum viable set (extend, but include at least):
    `der die das den dem des und oder nicht ist sind war ich du er sie es wir
    ihr mit auch noch schon eine einen einem eines kein keine für auf aus bei
    nach über unter vom zum zur dass weil aber dann wenn`.
    Use word boundaries (`\b…\b`, case-insensitive) so `die` doesn't match
    inside "diet" and `der` doesn't match "order".
- **Scoring rule:** `germanScore = (umlautPresent ? <weight> : 0) + stopwordHitCount`.
  Classify `german` when `germanScore >= <threshold>`, else `english`.
  Worker picks concrete weight/threshold during TDD; the rule must satisfy the
  ACs below (umlaut text → german; ≥2 distinct German stopwords → german; pure
  English/code → english).
- **Tie / very-short-text rule:** on a tie, an empty/whitespace string, or text
  below a small word-count floor (e.g. < N words — worker tunes), default to
  `english`. English is the safe default (8 built-in voices + the legacy
  convention is English).
- **Determinism:** pure function of the input string. No randomness, no I/O.

## Acceptance criteria

Detection (`Get-NarratorLanguage` / equivalent pure function):
- [ ] Text containing any umlaut or ß (e.g. "Die Änderung ist fertig") classifies as `german`.
- [ ] Text with ≥2 distinct German stopwords and no umlaut (e.g. "ich habe das nicht gemacht") classifies as `german`.
- [ ] Plain English prose (e.g. "I finished the refactor and all tests pass") classifies as `english`.
- [ ] A code-heavy / identifier-heavy English string does NOT false-positive as `german`.
- [ ] Empty string, whitespace-only, and a sub-floor very short string (e.g. "ok") classify as `english`.
- [ ] A genuine tie classifies as `english` (documented default).
- [ ] Stopword matching is whole-word and case-insensitive: "diet", "order", "IST-state" do NOT count `die`/`der`/`ist` as German hits.
- [ ] The function is pure (same input → same output, no network, no filesystem).

Voice-pair resolution (`Resolve-Voice` extended, per ADR 0028):
- [ ] Detected `english` with `./.claude/utterheim-voice` = `marius` resolves to `marius`.
- [ ] Detected `german` with `./.claude/utterheim-voice-de` = `juergen` resolves to `juergen`.
- [ ] Detected `german` with NO `utterheim-voice-de` and NO `$env:UTTERHEIM_VOICE_DE` falls back to the resolved English/default slot (the configured English voice, NOT a hard-coded `juergen`).
- [ ] Detected `german` with no DE slot AND English slot = `alba` → resolves to `alba` (fallback chains through the English resolution).
- [ ] `$env:UTTERHEIM_VOICE_DE` is honored for the German slot when the file is absent; the file wins over the env var when both present.
- [ ] Legacy behavior intact: a repo with ONLY `./.claude/utterheim-voice` (no DE slot, no detection-relevant German text) speaks exactly as before; `$env:UTTERHEIM_VOICE` and the `alba` default still work.
- [ ] An explicit `-Voice <id>` parameter overrides both slots and bypasses detection entirely.

Mute semantics:
- [ ] English slot = `off` mutes English utterances (shim exits 0 before the HTTP POST) while a real DE-slot voice still speaks German utterances.
- [ ] DE slot = `off` mutes German utterances while the English slot still speaks English.
- [ ] A repo with `utterheim-voice` = `off` and no DE slot is fully muted for all languages (German falls back to the `off` English slot → muted).
- [ ] `~/.utterheim/sound-disabled` global sentinel and the "waiting for your input" notification filter are unchanged.

`/narrator` command:
- [ ] `/narrator` (no arg) prints the catalog with each voice's `language` shown, and shows the current EN and DE slot for the repo.
- [ ] A documented invocation sets the German slot (writing `./.claude/utterheim-voice-de`), and a documented invocation sets the English/default slot (writing the legacy `./.claude/utterheim-voice`), without clobbering the other slot.
- [ ] When setting a slot, `/narrator` warns if the chosen voice's `language` (from `GET /voices`) doesn't match the slot it's being put in (e.g. an English voice written to the DE slot), but still persists (consistent with the existing "persist anyway + warn" behavior).
- [ ] `/narrator off` mutes the repo with the same effect as before (writes `off` to the English/default slot).
- [ ] Files are written plain UTF-8, no BOM (preserve the existing `-Encoding utf8` / Write-tool guidance).

Integration & packaging:
- [ ] Both hooks (`utterheim-stop.ps1`, `utterheim-notification.ps1`) run detection on their final spoken text and pass the result through to resolution (no detection on raw transcript JSON / markdown).
- [ ] `plugin.json` version is bumped from `0.1.0`.
- [ ] Hooks still always exit 0; `-Silent` still swallows all failures; no new runtime dependency is introduced.

## Notes

- **Decision of record:** ADR 0028 (`knowledge/decisions/0028-narrator-voice-pair-config.md`,
  scope main) fixes the on-disk format (legacy file = English/default slot; add
  `utterheim-voice-de` + `$env:UTTERHEIM_VOICE_DE`; German fallback → English
  voice, not `juergen`; mute evaluated post-resolution). Do not re-open these in
  implementation.
- **Server is untouched.** ADR 0023 keeps the speak body at `{text, voice}` and
  has the sidecar route by the voice's declared language. This task only changes
  which `voice` id the CLIENT sends. Confirm no sidecar / C# host work is in scope.
- **Related ADRs:** 0023 (voice carries language), 0024 (preload EN+DE),
  0025 (distilled `german`), 0003 (speak HTTP contract), 0028 (this task's
  config-format decision).
- **Prior art:** main-019 (original Claude hook kit / `utterheim-narrator`
  plugin being extended); main-035 (= ADR 0023, voice carries language),
  main-039 (sidecar multi-model serve / route by language), main-040 (`juergen`
  built-in + library `language` field), main-041 (Voices page language picker).
- **TDD guidance:** both detection and voice-pair resolution are pure functions.
  Extract `Get-NarratorLanguage` and the extended `Resolve-Voice` so they're
  callable in isolation and assert against the ACs above. Pester is the natural
  choice (PowerShell, no .NET project needed); the existing `Utterheim.Tests`
  xUnit project (main-044) is for the C# host and is the wrong home for plugin
  PowerShell — prefer a Pester spec colocated with the plugin scripts. Resolution
  tests should set `Get-Location` / temp `.claude/` dirs and env vars per case so
  the file-vs-env precedence and the German→English fallback are covered exactly
  as the ACs state.
- **Built-in voice facts** (for `/narrator` validation copy): 8 English built-ins
  `alba, marius, javert, jean, fantine, cosette, eponine, azelma`; `juergen` is
  the only built-in German voice — more German voices require cloning (ADR 0023).
- **Ubiquitous language:** "narrator," "voice," "slot" (English slot / German
  slot), "the resolved voice," "mute," "the sidecar routes by language." Avoid
  "locale"/"i18n" framing — this is voice selection, not localization.

## Outcome

The `utterheim-narrator` plugin (`claude-code-plugin/`) now narrates in a
language-matching voice. Implementation is **client-side only** — the speak
wire body stays `{text, voice}` and the sidecar routes by the voice's declared
language (ADR 0023). No server / C# / Python-sidecar change.

Key files:
- `scripts/narrator-lib.ps1` (new) — pure, dependency-free helpers, dot-sourced
  by the shim and the spec:
  - `Get-NarratorLanguage` — offline EN/DE detection. `germanScore = (umlaut/ß
    present ? 2 : 0) + distinctGermanStopwordHits`; classify `german` when
    `score >= 2`. Stopword matching is whole-word, case-insensitive (`\b…\b`),
    so `diet`/`order`/`IST-state` don't false-positive. Empty / whitespace /
    sub-2-word text and genuine ties default to `english`.
  - `Resolve-Voice` — two-slot resolution per ADR 0028 (English/default slot =
    legacy `utterheim-voice` → `$env:UTTERHEIM_VOICE` → `alba`; German slot =
    `utterheim-voice-de` → `$env:UTTERHEIM_VOICE_DE` → fall back to the resolved
    English voice, NOT a hard-coded `juergen`); explicit `-Voice` overrides and
    bypasses detection.
  - `Test-VoiceMuted` — `off`/`none`/`-` marker check, evaluated on the
    finally-resolved voice.
- `scripts/utterheim-speak.ps1` — dot-sources the lib; gained a `-Language`
  param; auto-detects when omitted; resolves the slot; mutes on the resolved
  voice before any HTTP POST. Hooks still always exit 0; `-Silent` unchanged.
- `scripts/utterheim-stop.ps1`, `scripts/utterheim-notification.ps1` — run
  detection on the final, markdown-stripped spoken string (never the raw
  transcript) and pass `-Language` through.
- `commands/narrator.md` — `/narrator` now sets/shows both slots (`/narrator
  <id>` = English, `/narrator de <id>` = German), shows each voice's `language`,
  and warns on a slot/language mismatch while still persisting. `/narrator off`
  mutes via the English/default slot as before. Files written plain UTF-8.
- `.claude-plugin/plugin.json` — version `0.1.0` → `0.2.0` (consumer-update
  trigger).
- `tests/narrator-lib.Tests.ps1` (new) — Pester spec, 21 tests covering every
  detection / resolution / mute AC. **All 21 pass** (Pester 3.4.0 on Windows
  PowerShell 5.1). Integration smoke also confirmed: a German summary in a repo
  whose English slot = `off` and no DE slot resolves through to the muted slot
  and exits 0 before any HTTP call.

Decision of record is ADR 0028 (config format, fallback, mute semantics). The
concrete heuristic weight/threshold/word-floor (2/2/2) are within the spec's
stated latitude, so no new ADR was warranted. No `examples/claude-hooks/`,
server, or sidecar changes.
