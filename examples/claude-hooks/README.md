# Claude Code → Mockingbird hooks

This directory is the **bridge from "the speak endpoint exists" to "my Claude
sessions actually talk to me"**. It ships:

- `mockingbird-hook.ps1` — a small PowerShell shim that POSTs `{text, voice}` to
  `http://127.0.0.1:7223/speak`.
- The wiring recipe below for Claude Code's **Stop** and **Notification** hooks.
- A worked example for two parallel sessions in audibly different voices —
  the v1 payoff (per `.agenthoff/vision.md`).

The transport is the one nailed down in **ADR 0003** (HTTP loopback on `:7223`,
`{text, voice}` body, no auth, single-user). If you want to skip the script
entirely, raw `curl` and the bundled `mockingbird-speak.exe` CLI both work
against the same endpoint.

---

## Prerequisites

1. Mockingbird is running (tray icon visible). The status footer in the tray
   window should read `HTTP 127.0.0.1:7223 • Engine: running` once the
   sidecar warms up.
2. PowerShell 5.1+ (ships with Windows) or PowerShell 7+.
3. Claude Code installed, with a config file you can edit (see "Wiring the
   hooks" below).

Sanity-check the endpoint before touching Claude config:

```powershell
.\mockingbird-hook.ps1 -Text "mockingbird is alive" -Voice alba
```

You should hear `alba` say it within ~1–2 s. If not, jump to **Troubleshooting**
at the bottom.

---

## Voice assignment — per-session via env var

Mockingbird does **not** know which Claude session is calling. Voice routing
is a caller-side convention: each terminal sets `MOCKINGBIRD_VOICE` before
launching `claude`, and the hook script reads it.

```powershell
# Terminal A — Claude session "alba"
$env:MOCKINGBIRD_VOICE = 'alba'
claude

# Terminal B — Claude session "marius"
$env:MOCKINGBIRD_VOICE = 'marius'
claude
```

Both sessions share the same `mockingbird-hook.ps1`. Each speaks in its own
voice because each inherited a different `MOCKINGBIRD_VOICE` from its parent
shell. That's the whole "session-distinguishing-by-ear" mechanism — there is
no server-side session identity in v1, by design.

The eight built-in voices (shipped with `pocket-tts` and listed by
`GET /voices`) are: `alba`, `marius`, `javert`, `jean`, `fantine`, `cosette`,
`eponine`, `azelma`. Use any of those, or any voice you've cloned through the
Voices page.

You can also pass `-Voice <id>` explicitly to override the env var, which is
handy when you want the same hook to *also* announce input-required prompts
in a different voice (see the Notification hook example below).

---

## Wiring the hooks in Claude Code

> **Verify against your Claude Code version.** The hook system has evolved.
> The shape below is the broad pattern documented for Claude Code's hook
> framework (`Stop` fires when the assistant finishes a turn; `Notification`
> fires when Claude needs your attention, e.g. an input-required prompt).
> Field names, file location, and exact event names may differ in your
> install — check `claude --help` or your Claude Code docs first. Treat
> this README as a recipe to **adapt**, not paste verbatim.

The general idea is: register an external command for the `Stop` and
`Notification` events that runs `mockingbird-hook.ps1`. A typical Claude
Code hook configuration block (in your project- or user-scoped Claude
settings file) looks something like:

```jsonc
{
  "hooks": {
    "Stop": [
      {
        "command": "powershell",
        "args": [
          "-NoProfile",
          "-ExecutionPolicy", "Bypass",
          "-File", "C:\\path\\to\\mockingbird\\examples\\claude-hooks\\mockingbird-hook.ps1",
          "-Text", "task done",
          "-Silent"
        ]
      }
    ],
    "Notification": [
      {
        "command": "powershell",
        "args": [
          "-NoProfile",
          "-ExecutionPolicy", "Bypass",
          "-File", "C:\\path\\to\\mockingbird\\examples\\claude-hooks\\mockingbird-hook.ps1",
          "-Text", "input required",
          "-Silent"
        ]
      }
    ]
  }
}
```

Notes:

- `-Silent` is recommended for hook contexts: a missing sidecar should
  never break Claude's own flow. The script will still exit 0 if the
  endpoint is unreachable, just without sound.
- We don't pass `-Voice` here — the script falls back to
  `$env:MOCKINGBIRD_VOICE`, which is exactly what you want for
  per-session voices.
- `-ExecutionPolicy Bypass` is scoped to this single invocation; it
  doesn't change machine-wide policy.
- If your Claude Code version uses a different schema (for example a
  single `command` string instead of `command` + `args`), translate
  accordingly. The substance — "run powershell with these arguments on
  Stop / Notification" — is portable.

---

## Worked example — two parallel sessions, two voices

This is the demo that makes the v1 payoff tangible.

**Terminal A** (PowerShell):

```powershell
$env:MOCKINGBIRD_VOICE = 'alba'
cd C:\some\repo-A
claude
# … work with Claude here. Each time it finishes a turn, alba says "task done".
```

**Terminal B** (a *separate* PowerShell window):

```powershell
$env:MOCKINGBIRD_VOICE = 'marius'
cd C:\some\repo-B
claude
# … work in parallel. Marius says "task done" on this side.
```

With both sessions running side-by-side, the same `Stop` hook fires in each,
and you can hear which session just finished without looking at the screen.
That's the entire feature.

If the two voices sound similar to your ear, swap one for `javert` (notably
different timbre) until you find a pair you can tell apart instantly. With
eight built-ins plus any voices you've cloned, finding a contrasting pair
takes seconds.

---

## Troubleshooting

These are the failure modes that surfaced during the clean-machine
verification of mockingbird's bootstrap (`main-018`). If something doesn't
sound right, work down the list.

### "Nothing happens — no error, no sound"

Most likely the hook fired but mockingbird isn't running. Run the script
manually without `-Silent`:

```powershell
.\mockingbird-hook.ps1 -Text "test" -Voice alba
```

If it prints `cannot reach http://127.0.0.1:7223/speak — is mockingbird
running?`, start mockingbird from the tray. The `-Silent` flag in your
hook config is *deliberately* swallowing this error so it doesn't break
Claude's flow — that's working as intended.

### "Tray icon is there but nothing speaks"

Open the tray window. The status footer should read
`HTTP 127.0.0.1:7223 • Engine: running`. Possible states:

- `Engine: starting` or `Engine: restarting` — sidecar is warming up.
  First-run downloads ~700 MB (Python embeddable + pocket-tts), so the
  initial bootstrap takes a few minutes. Subsequent launches start in
  seconds.
- `Engine: failed` — the sidecar crashed and the auto-restart watchdog
  gave up after 5 attempts. Check
  `%LOCALAPPDATA%\Mockingbird\logs\mockingbird-YYYYMMDD.log`; sidecar
  stdout/stderr appear under the `sidecar` source (Python tracebacks
  included since `main-021`).
- `Engine: stopping` immediately after Exit — expected; the host kills
  the python process tree (Win32 Job Object with `KILL_ON_JOB_CLOSE`,
  added in `main-022`) so you should not see leftover `python.exe` in
  Task Manager. If you do, that's a regression — file a bug.

### "First long sentence takes ages before audio starts"

Known issue. Currently the first chunk for a paragraph-length input can
take ~9 s on a cold sidecar (vision target is ≤2 s). Tracked as
**main-023**. Short sentences (and any subsequent calls once the
sidecar is warm) are typically ~1 s.

Workaround for hooks specifically: keep the hook text short ("task done",
"input required"). That sidesteps the long-input cold path entirely.

### "`/status` shows the sidecar is healthy, but my hook still doesn't speak"

Check that the hook script can actually be invoked. From the same shell
Claude Code is launched in:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\path\to\mockingbird\examples\claude-hooks\mockingbird-hook.ps1 `
  -Text "manual test"
```

If this works but the Claude Code hook doesn't, it's a Claude config
issue, not a mockingbird issue:

- Double-check the path is absolute and the JSON is valid (Claude Code
  silently ignores malformed hook config in some versions).
- Verify the hook event name (`Stop`, `Notification`, …) matches your
  Claude Code version.
- Some Claude Code versions log hook execution; check that log to see
  whether the hook is even firing.

### "Port 7223 is in use" on mockingbird startup

Something else grabbed the port first. Either stop the offender or
override the port in mockingbird's settings (`appsettings.json`). The
hook script reads `$env:MOCKINGBIRD_ENDPOINT` if you want to point it
at a non-default URL:

```powershell
$env:MOCKINGBIRD_ENDPOINT = 'http://127.0.0.1:17223'
```

### "Voice doesn't match what I set in `MOCKINGBIRD_VOICE`"

Environment variables are inherited at process spawn. If you set
`$env:MOCKINGBIRD_VOICE` *after* launching `claude`, the running session
won't see it — close and relaunch `claude` from the same shell. The
hook script reads the env var fresh on each invocation, so once the
parent shell's value is right, every subsequent hook call uses it.

### "The auto-restart watchdog is fighting `/status` polling"

Symptom: you kill the sidecar manually to test degraded behaviour, but
`/status` always reports `running` because the watchdog respawned the
sidecar before your poll arrived. This is expected — auto-restart
resilience is the more important contract. There's no "pause the
watchdog" knob in v1; if you need to observe a true degraded state,
disable the network so health probes fail, or attach a debugger.

---

## See also

- `.agenthoff/knowledge/decisions/0003-claude-transport-http.md` — the
  ADR that froze the wire format.
- `.agenthoff/contexts/main/README.md` — speak endpoint vocabulary,
  voice profile model, engine status.
- `src/Mockingbird.Cli/Program.cs` — the bundled `mockingbird-speak.exe`
  CLI, equivalent to this script for shells that prefer a binary.
