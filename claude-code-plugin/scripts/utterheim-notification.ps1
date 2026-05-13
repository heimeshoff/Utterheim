# utterheim-notification.ps1
#
# Claude Code Notification hook: reads the hook payload on stdin and speaks
# the notification message via utterheim-speak.ps1.
#
# Filters out the 60-second idle nag ("Claude is waiting for your input")
# so only real attention requests (permission prompts, etc.) get spoken.
#
# Honors the global sound toggle at ~/.utterheim/sound-disabled.
#
# Always exits 0 so a TTS hiccup never blocks Claude Code.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

try {
    $disableFlag = Join-Path $env:USERPROFILE '.utterheim\sound-disabled'
    if (Test-Path -LiteralPath $disableFlag) { exit 0 }

    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json -ErrorAction Stop
    $message = $payload.message
    if ([string]::IsNullOrWhiteSpace($message)) { exit 0 }

    if ($message -match '(?i)waiting for your input') { exit 0 }

    $speak = Join-Path $PSScriptRoot 'utterheim-speak.ps1'
    & $speak -Text $message -Silent | Out-Null
    exit 0
}
catch {
    exit 0
}
