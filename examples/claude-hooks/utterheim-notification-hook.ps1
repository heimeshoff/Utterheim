# utterheim-notification-hook.ps1
#
# Claude Code Notification hook: reads the hook payload on stdin and speaks
# the notification message via utterheim-hook.ps1.
#
# Filters out the 60-second idle nag ("Claude is waiting for your input")
# so only real attention requests (permission prompts, etc.) get spoken.
#
# Voice routing: same env-var contract as utterheim-hook.ps1 — set
# UTTERHEIM_VOICE in the calling shell (or via Claude Code's settings.json
# env block) and it will be honored.
#
# Always exits 0 so a TTS hiccup or missing sidecar can never block Claude Code.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json -ErrorAction Stop
    $message = $payload.message
    if ([string]::IsNullOrWhiteSpace($message)) { exit 0 }

    # Drop the 60s idle reminder. Claude Code emits this when the prompt has
    # been sitting idle — it isn't a real attention request, just a nag.
    if ($message -match '(?i)waiting for your input') { exit 0 }

    $hookScript = Join-Path $PSScriptRoot 'utterheim-hook.ps1'
    & $hookScript -Text $message -Silent | Out-Null
    exit 0
}
catch {
    exit 0
}
