---
description: Pick the narrator voice (via Utterheim) for this repo's Claude Code TTS notifications, or `off` to mute
argument-hint: "[voice-name | off]"
allowed-tools: PowerShell, Bash, Write
---

Set the per-repo narrator voice (the one Utterheim speaks Claude's end-of-turn summaries and attention prompts in). The selection is written to `./.claude/utterheim-voice` (a single line containing the voice id, or the literal `off` to mute this repo) and read on every hook fire by `scripts/utterheim-speak.ps1` — no Claude restart needed.

**Cross-platform note:** Utterheim is a Windows-only WPF app. On macOS / Linux the PowerShell hook scripts won't run at all (no `powershell` on PATH), so you'll get silent no-ops by default — no opt-out needed. The `off` value is mainly useful on Windows when you want to mute *this* repo while leaving the global default (`$env:UTTERHEIM_VOICE`) intact for other repos.

## Behavior

Interpret `$ARGUMENTS`:

- **`off` / `none` / `-`** — write `off` to `./.claude/utterheim-voice`. Done.
- **Any other non-empty value** — treat it as a voice id. Fetch the catalog to validate (warn if not found, but still write). Persist.
- **Empty** — fetch the catalog, print it as a plain two-category text list, end the turn. Do **not** use `AskUserQuestion` — the user wants to read the list and type the voice they want.

## Step 1 — fetch the voice catalog

On Windows, prefer the `PowerShell` tool to avoid Bash-quoting issues with `$_` and `$(...)`. Run:

```powershell
try { (Invoke-WebRequest -Uri http://127.0.0.1:7223/voices -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop).Content } catch { "UNREACHABLE: $($_.Exception.Message)" }
```

On non-Windows or if the PowerShell tool isn't available, fall back to `Bash`:

```bash
curl -s --max-time 3 http://127.0.0.1:7223/voices || echo "UNREACHABLE"
```

The response is a JSON array of voice objects: `{id, name, engine, isBuiltIn}`. Built-ins ship with pocket-tts (alba, marius, javert, jean, fantine, cosette, eponine, azelma); the rest are user-cloned voices.

**If you get `UNREACHABLE:`** — Utterheim isn't running. Tell the user "Utterheim isn't reachable at 127.0.0.1:7223 — start the Utterheim tray app and try again." Stop here.

## Step 2 — print the list (when no argument was given)

Parse the JSON and partition into two groups by `isBuiltIn`. Print exactly this format (no fences, no AskUserQuestion, no extra commentary):

```
Original voices:
  alba, marius, javert, jean, fantine, cosette, eponine, azelma

Custom voices:
  (none)

Type `/narrator <name>` to set, or `/narrator off` to mute this repo.
```

Rules for the output:

- Both categories always appear. Use `(none)` when a category is empty.
- Names are the **ids**, comma-separated on a single indented line (wrap only if the line would exceed ~80 chars).
- Sort each group by id alphabetically, except keep pocket-tts built-ins in their canonical order (`alba, marius, javert, jean, fantine, cosette, eponine, azelma`) since users tend to know that order.
- No emojis. No bold. No tables. Plain prose so it reads cleanly in any client.

End the turn after the print. The user will re-invoke `/narrator <name>` with their pick.

## Step 3 — persist (only when an argument was given)

Write the chosen voice id (or the literal `off`) to `./.claude/utterheim-voice`. Create the `.claude/` directory if it doesn't exist. The file is a single line, no trailing newline required, plain UTF-8 (NOT UTF-16 BOM — pass `-Encoding utf8` if using PowerShell `Set-Content`/`Out-File`, or use the `Write` tool directly).

## Step 4 — confirm (only when persisting)

Report in one short line, e.g. `narrator: marius`, `narrator: marius (cloned)`, or `narrator: off (this repo is muted)`. Mention that it takes effect on the very next hook fire.

If the supplied name wasn't in the catalog, persist it anyway but warn: `narrator: <name> (warning: not in current catalog — typo, or voice was deleted?)`.
