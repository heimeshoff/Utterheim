---
description: Pick the narrator voices (English + German) Utterheim speaks Claude Code's TTS in for this repo, or `off` to mute
argument-hint: "[voice-name | de <voice-name> | off]"
allowed-tools: PowerShell, Bash, Write
---

Set the per-repo narrator voices. Utterheim narrates Claude's end-of-turn summaries and attention prompts; the plugin detects whether each spoken string is **English** or **German** and speaks it in the matching slot's voice (the sidecar routes by the voice's declared language). So each repo configures a PAIR of slots:

- **English / default slot** → `./.claude/utterheim-voice` (the legacy file; a single line with a voice id, or `off`/`none`/`-` to mute).
- **German slot** → `./.claude/utterheim-voice-de` (optional; same format). When absent, German utterances fall back to the English/default voice — never a surprise `juergen`.

Both files are read on every hook fire by `scripts/utterheim-speak.ps1` — no Claude restart needed.

**Cross-platform note:** Utterheim is a Windows-only WPF app. On macOS / Linux the PowerShell hook scripts won't run at all (no `powershell` on PATH), so you'll get silent no-ops by default — no opt-out needed.

## Behavior

Interpret `$ARGUMENTS`:

- **Empty** — fetch the catalog, print it as a plain text list (with each voice's language) plus the repo's current EN and DE slot, then end the turn. Do **not** use `AskUserQuestion`.
- **`off` / `none` / `-`** — write `off` to the English/default slot `./.claude/utterheim-voice`. This mutes the repo (German falls back to the muted English slot). Done.
- **`de <voice-id>`** — set the **German** slot: write `<voice-id>` to `./.claude/utterheim-voice-de`. `de off` writes `off` there to mute German only.
- **Any other single non-empty value** — treat it as a voice id for the **English/default** slot: write it to `./.claude/utterheim-voice`.

Setting one slot never clobbers the other.

## Step 1 — fetch the voice catalog

On Windows, prefer the `PowerShell` tool to avoid Bash-quoting issues with `$_` and `$(...)`. Run:

```powershell
try { (Invoke-WebRequest -Uri http://127.0.0.1:7223/voices -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop).Content } catch { "UNREACHABLE: $($_.Exception.Message)" }
```

On non-Windows or if the PowerShell tool isn't available, fall back to `Bash`:

```bash
curl -s --max-time 3 http://127.0.0.1:7223/voices || echo "UNREACHABLE"
```

The response is a JSON array of voice objects: `{id, name, engine, language, isBuiltIn}`. The `language` attribute (`english` / `german`) tells you which slot a voice belongs in. Built-ins shipped with pocket-tts: 8 English — `alba, marius, javert, jean, fantine, cosette, eponine, azelma`; 1 German — `juergen`. More German voices require cloning.

**If you get `UNREACHABLE:`** — Utterheim isn't running. Tell the user "Utterheim isn't reachable at 127.0.0.1:7223 — start the Utterheim tray app and try again." Stop here.

## Step 2 — print the list (when no argument was given)

Parse the JSON. Read the repo's current slots: the first line of `./.claude/utterheim-voice` (English/default; `(unset → alba)` if missing) and of `./.claude/utterheim-voice-de` (German; `(unset → falls back to English slot)` if missing). Partition voices into two groups by `language`. Print exactly this shape (no fences, no AskUserQuestion, no extra commentary):

```
English voices:
  alba, marius, javert, jean, fantine, cosette, eponine, azelma

German voices:
  juergen

Current repo:
  English slot: marius
  German slot:  (unset → falls back to English slot)

Type `/narrator <name>` to set the English voice, `/narrator de <name>` for German, or `/narrator off` to mute this repo.
```

Rules for the output:

- All three sections always appear. Use `(none)` for an empty voice group; show the unset notes above for empty slots.
- Names are the **ids**, comma-separated on a single indented line (wrap only if it would exceed ~80 chars).
- Keep pocket-tts built-ins in their canonical order; sort cloned voices alphabetically.
- No emojis. No bold. No tables. Plain prose.

End the turn after the print.

## Step 3 — persist (only when an argument was given)

- English/default slot → write the voice id (or `off`) to `./.claude/utterheim-voice`.
- German slot (`de <id>`) → write the voice id (or `off`) to `./.claude/utterheim-voice-de`.

Create the `.claude/` directory if it doesn't exist. Each file is a single line, plain **UTF-8, no BOM** (pass `-Encoding utf8` if using PowerShell `Set-Content`/`Out-File`, or use the `Write` tool directly — NOT UTF-16 BOM).

## Step 4 — confirm + language validation (only when persisting)

Report in one short line, e.g. `narrator (en): marius`, `narrator (de): juergen`, or `narrator: off (this repo is muted)`. Mention it takes effect on the very next hook fire.

Validation against the catalog's `language` attribute (persist anyway, but warn):

- If the supplied name isn't in the catalog: `narrator (en): <name> (warning: not in current catalog — typo, or voice was deleted?)`.
- If the chosen voice's `language` doesn't match the slot it's being written to — e.g. an English voice put in the German slot, or vice versa: `narrator (de): marius (warning: 'marius' is an english voice; German utterances may be mispronounced — consider a german voice like juergen)`. Still persist.
