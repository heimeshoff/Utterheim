---
id: 0028
title: Narrator stores the EN+DE voice pair as two sibling files; legacy single file is the English/default slot
scope: main
status: accepted
date: 2026-05-24
supersedes: []
superseded_by: []
related_tasks: [main-047]
related_research: [pocket-tts-german-support-2026-05-18]
---

# ADR 0028: Narrator stores the EN+DE voice pair as two sibling files; the legacy single `utterheim-voice` file is the English / default slot

## Context

The vision's core feature is that each parallel Claude Code session sounds
distinct **by ear** — a per-repo narrator voice, set via the `/narrator`
slash command, written to `./.claude/utterheim-voice` and read on every hook
fire by `scripts/utterheim-speak.ps1` (shipped in the `utterheim-narrator`
plugin, main-019).

Utterheim now serves two languages concurrently (ADR 0024) and routes each
`POST /speak` to the resident model by the named voice's declared `language`
(ADR 0023; the wire body stays `{text, voice}`). Built-ins: 8 English voices
(`alba, marius, javert, jean, fantine, cosette, eponine, azelma`) plus
`juergen` (German, ADR 0023 / main-040). `GET /voices` now returns a
`language` attribute per voice (english/german).

main-047 adds language-aware narration: the plugin hook classifies the
already-resolved spoken text as German or English (lightweight PowerShell
heuristic, no dependency) and picks the matching voice. For that to work the
narrator must configure **two** voices per repo — one English, one German —
because a voice is language-bound. The open question this ADR closes: **how
is the EN+DE pair represented on disk and across the voice-resolution chain,
without breaking the thousands-of-existing-repos single-`utterheim-voice`
convention?**

Constraints:

- The existing `./.claude/utterheim-voice` single-line file and
  `$env:UTTERHEIM_VOICE` must keep working unchanged for repos that never
  re-run `/narrator`.
- The `off` / `none` / `-` per-repo mute marker must survive.
- `utterheim-speak.ps1` runs in Windows PowerShell 5.1 hook contexts; the
  format must be trivially readable there with no parser dependency.
- `/narrator` writes the file(s) with the `Write` tool or `Set-Content`;
  the format must be writable by an LLM-driven slash command without
  fragile escaping.

## Decision

**Two sibling files, one per language slot, plus the legacy file as the
English / default slot.**

On-disk layout under `./.claude/`:

- `utterheim-voice` — the **English / default** slot. Unchanged meaning:
  a single line containing a voice id, or `off` / `none` / `-` to mute.
  This is exactly the legacy file; existing repos keep working with no
  migration.
- `utterheim-voice-de` — the **German** slot. Same single-line format
  (voice id, or `off` / `none` / `-`). Optional: absent means "no German
  voice configured for this repo."

(No `utterheim-voice-en` file. The English slot **is** the legacy
`utterheim-voice` file, so that one filename carries both the back-compat
contract and the English-slot role. Introducing a third file would create
two competing English sources with no ordering rule.)

Environment-variable mirror, resolved when the per-repo file for a slot is
absent:

- `$env:UTTERHEIM_VOICE` — English / default slot (legacy, unchanged).
- `$env:UTTERHEIM_VOICE_DE` — German slot.

**Resolution chain (per detected language), first hit wins:**

English (or default / unknown):
1. `-Voice` parameter (explicit override; bypasses detection)
2. `./.claude/utterheim-voice`
3. `$env:UTTERHEIM_VOICE`
4. `alba`

German (only when detection picks German):
1. `-Voice` parameter (explicit override; bypasses detection)
2. `./.claude/utterheim-voice-de`
3. `$env:UTTERHEIM_VOICE_DE`
4. **fall back to the resolved English/default slot** (steps 2–4 above) —
   *not* a hard-coded `juergen`. See "German fallback" below.

**Mute interaction:** the `off` / `none` / `-` marker is evaluated on the
**finally-resolved** voice for the detected language. If the English slot is
`off`, an English utterance is muted; the German slot, if set to a real
voice, still speaks German utterances (and vice-versa). A repo that sets only
`utterheim-voice` to `off` and has no `utterheim-voice-de` is fully muted
(English `off`; German falls back to the `off` English slot). This makes
"`/narrator off` mutes the repo" hold exactly as before.

**German fallback rationale:** when German is detected but no German voice is
configured, falling back to the *configured English voice* (rather than
`juergen`) keeps the session's chosen identity and never surprises a user
who never opted into German by suddenly speaking in `juergen`. The cost — a
German sentence read by an English-tagged voice, which the sidecar will route
to the English model and produce English-accented German — is acceptable
because it only happens when the user has *not* configured German, i.e. has
implicitly said "I don't care about German here." Configuring
`utterheim-voice-de juergen` is the one-step opt-in.

## Consequences

### Positive
- **Zero migration for existing repos.** Every `./.claude/utterheim-voice`
  and `$env:UTTERHEIM_VOICE` keeps meaning exactly what it meant. The new
  capability is purely additive — a German slot that defaults to "unset →
  fall back to English."
- **Trivial to read in the 5.1 hook.** Each slot is a single-line file, the
  same `Get-Content -Raw | Trim` the shim already does. No JSON/INI parser,
  no schema, no BOM hazard beyond what `/narrator` already handles.
- **Trivial to write from `/narrator`.** Setting the German slot is "write a
  voice id to one more file," the same Write-tool path the command already
  uses. No structured-document editing.
- **Mute semantics compose per language** without a special case: the marker
  lives in whichever slot file, and is checked after resolution.
- **The English slot keeps a single canonical home.** No "which English
  source wins, the legacy file or a new `-en` file" tie-break to invent or
  document.

### Negative
- **Two files for the fully-configured case.** A repo that wants distinct
  EN and DE voices has two marker files in `.claude/`. Mild clutter; both
  are git-ignorable / per-developer and already the norm for `utterheim-voice`.
- **The English slot's filename does double duty** (back-compat contract +
  English-slot role), which a newcomer reading only the filename might not
  guess. Mitigated by `/narrator` output and the shim's header comment
  spelling it out.
- **German-without-DE-slot speaks in an English voice** (English-accented
  German via the English model). Accepted as the explicit-opt-in cost above;
  it is strictly better than silence and strictly better than hijacking the
  session identity with `juergen`.

### Neutral
- No server change. ADR 0023's `{text, voice}` contract is untouched; this
  ADR only concerns how the *client* picks which `voice` id to send.
- The detection heuristic (which language) is orthogonal to this ADR (which
  voice per language) and is specified in main-047, not here.

## Alternatives considered

1. **Two sibling files + legacy file = English slot (selected).** See above.

2. **One structured file** (`utterheim-voice.json` /
   `{ "en": "...", "de": "..." }`, or an INI `en=…\nde=…`). Rejected: forces
   a parser into the 5.1 hook (the shim currently does a one-line
   `Get-Content`), and a migration story for the millions of existing plain
   single-line files (detect "is this JSON or a bare id?"). The plain-line
   format is the one thing every existing repo and `$env:UTTERHEIM_VOICE`
   already speak; keeping it as the English slot is free.

3. **Extend the single `utterheim-voice` file with a second line / delimiter**
   (`alba` on line 1, `juergen` on line 2; or `alba|juergen`). Rejected:
   ambiguous against existing single-line files (is line 2 a German voice or
   a stray newline?), and a delimiter inside the value collides with the
   `off`/`none`/`-` markers and with any future voice id containing the
   delimiter. The implicit "first line is English" rule is exactly the
   fragility the two-file split avoids.

4. **`utterheim-voice-en` + `utterheim-voice-de`, legacy file read only as a
   fallback for the EN slot.** Rejected: creates two English sources
   (`-en` file vs legacy file) needing an ordering rule, and tempts
   `/narrator` to write `-en` while leaving a stale legacy file that other
   tooling still reads. One canonical English home (the legacy file) is
   simpler and back-compat by construction.

5. **Hard-code `juergen` as the German fallback** when no DE slot is set.
   Rejected as the *default*: it overrides the user's chosen session voice
   for any German sentence, breaking the "each session sounds like its
   chosen voice" feature for users who never opted into German. The selected
   design falls back to the configured English voice instead; `juergen` is
   what the user explicitly writes into the DE slot when they want it.

## References
- ADR 0003 — speak HTTP API `{text, voice}`. Unchanged by this ADR.
- ADR 0023 — voice profile carries its language; sidecar routes by voice.
  This ADR is the client-side counterpart: it decides how the client stores
  *two* voices so it can name the language-matching one.
- ADR 0024 / ADR 0025 — sidecar preloads English + distilled German; defines
  the two languages whose slots this ADR configures.
- main-019 — original Claude Code hook kit / `utterheim-narrator` plugin that
  this ADR extends.
- main-047 — language-aware narration (detection heuristic + voice-pair
  resolution); the task that consumes this ADR.
- Research: `pocket-tts-german-support-2026-05-18` — §plugin-integration
  notes on the per-language voice surface.
