# utterheim-speak.ps1
#
# Core shim that POSTs {text, voice} to the Utterheim speak endpoint
# (default http://127.0.0.1:7223/speak). Bundled inside the utterheim-narrator
# plugin so consumers don't need a path to the utterheim repo.
#
# Language-aware (ADR 0028): the spoken text is classified EN/DE and a voice is
# resolved from the matching slot. The sidecar then routes by the voice's
# declared language (ADR 0023) — the wire body stays {text, voice}.
#
# Voice resolution (first hit wins), per detected/-Language slot:
#   English/default slot:
#     1. -Voice parameter (explicit override; bypasses detection)
#     2. ./.claude/utterheim-voice          (project-local, written by /narrator)
#     3. $env:UTTERHEIM_VOICE
#     4. "alba"                             (pocket-tts default)
#   German slot (only when language is german):
#     1. -Voice parameter
#     2. ./.claude/utterheim-voice-de
#     3. $env:UTTERHEIM_VOICE_DE
#     4. fall back to the resolved English/default slot
#
# -Language: 'german' | 'english'. When omitted, the text is auto-detected via
# Get-NarratorLanguage. Hooks pass their already markdown-stripped final string.
#
# Exit codes:
#   0  speak request accepted, -Silent swallowed an error, or voice is muted
#   2  HTTP error from utterheim (validation / server error)
#   3  cannot reach utterheim (sidecar not running, port in use, etc.)
#
# With -Silent (recommended for hook contexts), all failures exit 0.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Text,

    [string]$Voice,

    [string]$Language,

    [string]$Endpoint,

    [switch]$Silent,

    [int]$TimeoutSec = 3
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'narrator-lib.ps1')

# Detect the language of the spoken text unless an explicit voice override is
# in play (an override bypasses detection entirely) or -Language was supplied.
if ([string]::IsNullOrWhiteSpace($Language)) {
    $Language = Get-NarratorLanguage -Text $Text
}

$Voice = Resolve-Voice -Explicit $Voice -Language $Language

# Mute is evaluated on the FINALLY-resolved voice for the detected language
# (ADR 0028): `off`/`none`/`-` in the resolved slot skips the call entirely.
# A repo's English slot = `off` mutes English while a real DE slot still speaks.
if (Test-VoiceMuted $Voice) { exit 0 }

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    if ($env:UTTERHEIM_ENDPOINT) { $Endpoint = $env:UTTERHEIM_ENDPOINT }
    else { $Endpoint = 'http://127.0.0.1:7223' }
}

$url = ($Endpoint.TrimEnd('/')) + '/speak'
$body = @{ text = $Text; voice = $Voice } | ConvertTo-Json -Compress
# Send as UTF-8 bytes: Windows PowerShell 5.1's Invoke-WebRequest mangles
# non-ASCII string bodies (em-dashes, smart quotes), causing HTTP 400.
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)

try {
    $response = Invoke-WebRequest `
        -Uri $url `
        -Method Post `
        -ContentType 'application/json; charset=utf-8' `
        -Body $bodyBytes `
        -TimeoutSec $TimeoutSec `
        -UseBasicParsing `
        -ErrorAction Stop

    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
        exit 0
    }

    if ($Silent) { exit 0 }
    Write-Error "utterheim-speak: HTTP $($response.StatusCode) from $url"
    exit 2
}
catch [System.Net.WebException] {
    if ($Silent) { exit 0 }
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($status) {
        Write-Error "utterheim-speak: HTTP $status from $url - $($_.Exception.Message)"
        exit 2
    }
    Write-Error "utterheim-speak: cannot reach $url - is utterheim running? ($($_.Exception.Message))"
    exit 3
}
catch {
    if ($Silent) { exit 0 }
    Write-Error "utterheim-speak: $($_.Exception.Message)"
    exit 3
}
