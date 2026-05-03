# mockingbird-hook.ps1
#
# Minimal Claude Code hook script: posts a {text, voice} speak request to
# the mockingbird speak endpoint at http://127.0.0.1:7223/speak.
#
# Per ADR 0003 the wire format is the published language. This script is a
# convenience shim — `curl` or the bundled `mockingbird-speak.exe` CLI work
# equally well.
#
# Voice routing convention (caller-side): each shell sets MOCKINGBIRD_VOICE
# before launching `claude`. The hook reads it and forwards. No server-side
# session identity exists; per-session voice is purely an env-var contract
# between the user's shell and this script.
#
# Usage:
#   .\mockingbird-hook.ps1 -Text "task done"
#   .\mockingbird-hook.ps1 -Text "input required" -Voice marius
#   .\mockingbird-hook.ps1 -Text "task done" -Silent
#
# Exit codes:
#   0  speak request accepted (HTTP 202)
#   2  HTTP error from mockingbird (validation / server error)
#   3  cannot reach mockingbird (sidecar not running, port in use elsewhere, etc.)
#
# With -Silent, *all* failures exit 0 — for hook contexts where a missing
# sidecar must never block Claude Code itself.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Text,

    # Voice id. Defaults to $env:MOCKINGBIRD_VOICE, then "alba" (a pocket-tts
    # built-in that ships with mockingbird out of the box).
    [string]$Voice,

    # Override the endpoint base URL. Defaults to $env:MOCKINGBIRD_ENDPOINT,
    # then http://127.0.0.1:7223 (ADR 0003 default).
    [string]$Endpoint,

    # Swallow all errors and always exit 0. Recommended for Stop / Notification
    # hooks so Claude Code's normal flow is never blocked by a TTS hiccup.
    [switch]$Silent,

    # Connect+request timeout in seconds. Speak is fire-and-forget (the server
    # returns 202 immediately) so this only needs to cover the round trip.
    [int]$TimeoutSec = 3
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Voice)) {
    if ($env:MOCKINGBIRD_VOICE) { $Voice = $env:MOCKINGBIRD_VOICE }
    else { $Voice = 'alba' }
}

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    if ($env:MOCKINGBIRD_ENDPOINT) { $Endpoint = $env:MOCKINGBIRD_ENDPOINT }
    else { $Endpoint = 'http://127.0.0.1:7223' }
}

$url = ($Endpoint.TrimEnd('/')) + '/speak'
$body = @{ text = $Text; voice = $Voice } | ConvertTo-Json -Compress

try {
    $response = Invoke-WebRequest `
        -Uri $url `
        -Method Post `
        -ContentType 'application/json' `
        -Body $body `
        -TimeoutSec $TimeoutSec `
        -UseBasicParsing `
        -ErrorAction Stop

    # Mockingbird returns 202 Accepted with {requestId, queuePosition}.
    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
        exit 0
    }

    Write-Error "mockingbird-hook: HTTP $($response.StatusCode) from $url"
    if (-not $Silent) { exit 2 } else { exit 0 }
}
catch [System.Net.WebException] {
    # Server reachable but rejected the request (e.g. 400 missing text).
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($Silent) { exit 0 }
    if ($status) {
        Write-Error "mockingbird-hook: HTTP $status from $url — $($_.Exception.Message)"
        exit 2
    }
    Write-Error "mockingbird-hook: cannot reach $url — is mockingbird running? ($($_.Exception.Message))"
    exit 3
}
catch {
    if ($Silent) { exit 0 }
    Write-Error "mockingbird-hook: $($_.Exception.Message)"
    exit 3
}
