# utterheim-speak.ps1
#
# Core shim that POSTs {text, voice} to the Utterheim speak endpoint
# (default http://127.0.0.1:7223/speak). Bundled inside the utterheim-narrator
# plugin so consumers don't need a path to the utterheim repo.
#
# Voice resolution order (first hit wins):
#   1. -Voice parameter
#   2. ./.claude/utterheim-voice             (project-local, written by /narrator)
#   3. $env:UTTERHEIM_VOICE
#   4. "alba"                                (pocket-tts default)
#
# Exit codes:
#   0  speak request accepted, or -Silent swallowed an error
#   2  HTTP error from utterheim (validation / server error)
#   3  cannot reach utterheim (sidecar not running, port in use, etc.)
#
# With -Silent (recommended for hook contexts), all failures exit 0.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Text,

    [string]$Voice,

    [string]$Endpoint,

    [switch]$Silent,

    [int]$TimeoutSec = 3
)

$ErrorActionPreference = 'Stop'

function Resolve-Voice {
    param([string]$Explicit)

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit }

    $projectFile = Join-Path (Get-Location).Path '.claude\utterheim-voice'
    if (Test-Path -LiteralPath $projectFile) {
        $v = (Get-Content -LiteralPath $projectFile -Raw -ErrorAction SilentlyContinue)
        if ($v) {
            $v = $v.Trim()
            if (-not [string]::IsNullOrWhiteSpace($v)) { return $v }
        }
    }

    if ($env:UTTERHEIM_VOICE) { return $env:UTTERHEIM_VOICE }

    return 'alba'
}

$Voice = Resolve-Voice -Explicit $Voice

# Per-repo opt-out: `./.claude/utterheim-voice` containing "off" / "none" / "-"
# tells us to skip the call entirely. Used on machines without Utterheim
# (e.g. macOS, where PowerShell isn't installed and these scripts can't even
# run — but the marker is the documented disable signal).
if ($Voice -match '^(off|none|-)$') { exit 0 }

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    if ($env:UTTERHEIM_ENDPOINT) { $Endpoint = $env:UTTERHEIM_ENDPOINT }
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
