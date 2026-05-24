# utterheim-narrator

A Claude Code plugin that speaks Claude's end-of-turn summaries and attention prompts aloud through [Utterheim](../README.md), the local TTS sidecar. Installed once into Claude Code; works in any project.

## How it works

- A `Stop` hook fires when Claude finishes a turn — the script reads the last assistant text from the session transcript and POSTs it to `http://127.0.0.1:7223/speak`.
- A `Notification` hook fires on permission prompts and similar attention requests, and speaks the message. The 60-second idle nag (`"Claude is waiting for your input"`) is filtered out.
- **Language-aware:** each spoken string is classified English or German by an offline, dependency-free heuristic; the plugin then picks the matching voice from the repo's English/German slot pair, and Utterheim's sidecar routes it to the right resident model. So a German summary gets a German voice instead of English-accented German.
- The Utterheim tray app must be running for sound. Without it the hooks silently no-op.

## Install

From inside Claude Code, in the project where you want the plugin:

```
/plugin marketplace add <path-to-utterheim-repo>/claude-code-plugin
/plugin install utterheim-narrator@utterheim-narrator
```

`<path-to-utterheim-repo>` is the absolute path to a local clone (e.g. `C:/src/heimeshoff/tooling/utterheim`) or a `git` URL. The marketplace lives inside the `claude-code-plugin/` subdirectory of the Utterheim repo, so the plugin tracks the same source as the tray app itself. Restart Claude Code after installing so hooks are picked up.

### Updates

Local/third-party marketplaces have auto-update **disabled** by default. To enable it, run `/plugin`, go to the **Marketplaces** tab, select `utterheim-narrator`, and choose "Enable auto-update". Claude Code will then refresh on startup and prompt you to run `/reload-plugins` when there's a new version.

To update manually:

```
/plugin marketplace update utterheim-narrator
/plugin update utterheim-narrator@utterheim-narrator
/reload-plugins
```

The plugin has its own `version` field in `.claude-plugin/plugin.json`. Claude Code only prompts an update when *that* bumps — commits to the Utterheim app that don't change the plugin won't churn consumers.

## Picking narrator voices per repo

```
/narrator              # print the catalog + this repo's EN and DE slots
/narrator marius       # set the English/default voice
/narrator de juergen   # set the German voice
/narrator off          # mute this repo (also: none, -)
```

Each repo configures a **pair of voice slots**; the detected language of the spoken text picks within the pair:

- **English / default slot** → `./.claude/utterheim-voice` (the legacy file).
- **German slot** → `./.claude/utterheim-voice-de` (optional).

Both are read on every hook fire — no Claude restart needed. Voice resolution in `scripts/utterheim-speak.ps1` (first hit wins, per detected language):

English (or default / unknown):

1. Explicit `-Voice` parameter (bypasses detection)
2. `./.claude/utterheim-voice` (project-local, written by `/narrator`)
3. `$env:UTTERHEIM_VOICE`
4. `alba` (pocket-tts default)

German (only when detection picks German):

1. Explicit `-Voice` parameter (bypasses detection)
2. `./.claude/utterheim-voice-de`
3. `$env:UTTERHEIM_VOICE_DE`
4. fall back to the resolved English/default slot (keeps the session's chosen identity — never a surprise `juergen`)

A value of `off` / `none` / `-` is an explicit disable, evaluated on the **finally-resolved** voice for the detected language: the speak script exits before any HTTP call. So an English slot of `off` mutes English while a real German slot still speaks German, and a repo with only `utterheim-voice` = `off` (no German slot) is fully muted (German falls back to the muted English slot).

Built-in voices shipped with pocket-tts: 8 English — `alba`, `marius`, `javert`, `jean`, `fantine`, `cosette`, `eponine`, `azelma`; 1 German — `juergen`. Cloned voices made through Utterheim's Voices page also appear in `/narrator` (more German voices require cloning).

## Muting everywhere

```powershell
# Global mute — both hooks honor this sentinel file
New-Item -ItemType File "$env:USERPROFILE\.utterheim\sound-disabled" -Force

# Unmute
Remove-Item "$env:USERPROFILE\.utterheim\sound-disabled"
```

## Platform support

Utterheim is a Windows-only WPF app, and the hooks shell out to PowerShell. On **macOS / Linux** without PowerShell installed, the hook commands fail to spawn and Claude treats that as a no-op — you get silence by default, no opt-out needed. `/narrator off` is mainly useful on Windows for per-repo muting.

## Layout

```
.claude-plugin/plugin.json            # plugin manifest
hooks/hooks.json                      # Stop + Notification hooks → Utterheim
scripts/narrator-lib.ps1              # pure helpers: language detection + voice-slot resolution
scripts/utterheim-speak.ps1           # detects language, resolves slot, POSTs {text, voice}
scripts/utterheim-stop.ps1            # speaks Claude's end-of-turn summary
scripts/utterheim-notification.ps1    # speaks attention prompts (filters idle nag)
commands/narrator.md                  # /narrator slash command
tests/narrator-lib.Tests.ps1          # Pester spec for the pure helpers
```

## Migrating from agentheim

If you previously had this functionality through the `agentheim` plugin:

1. `/plugin uninstall agentheim@agentheim` (or upgrade to the renamed `agentheim` which no longer carries the narrator).
2. Install this plugin as shown above.
3. In each repo where you had a voice pinned, rename `./.claude/agentheim-voice` → `./.claude/utterheim-voice`.
4. If you had a global mute, rename `~/.agentheim/sound-disabled` → `~/.utterheim/sound-disabled`.
5. Replace any `$env:UTTERHEIM_VOICE` / `$env:UTTERHEIM_ENDPOINT` env vars with `UTTERHEIM_VOICE` / `UTTERHEIM_ENDPOINT`. To set a global German voice too, add `$env:UTTERHEIM_VOICE_DE`.
