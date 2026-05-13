<#
.SYNOPSIS
    Measure first-chunk latency end-to-end for a single utterheim speak request.

.DESCRIPTION
    Runs the measurement protocol pinned in main-023:
      T0 = moment HTTP POST /speak is sent (captured here, just before curl.exe).
      T1 = moment first PCM chunk lands at the audio device, identified by the
           Serilog line containing the literal substring "FIRST-AUDIO-DISPATCH"
           that AudioPlayer.cs emits exactly once per request.
      first-chunk latency (end-to-end) = T1 - T0.

    Also captures curl.exe's own time_starttransfer and time_total so the
    HTTP-side timings line up with the engine's perspective.

    The script is deliberately mechanical: it does only what main-023 says,
    in the order main-023 says it. It does not attempt to fix anything.

.PARAMETER InputFile
    Path to a UTF-8 .txt file whose entire content is the speak text.

.PARAMETER Voice
    Pocket-tts voice id. Default: alba (one of the eight built-ins).

.PARAMETER Endpoint
    Utterheim speak endpoint base URL. Default: http://127.0.0.1:7223 (ADR 0003).

.PARAMETER Repeat
    Number of measurement passes. Default: 1. With Repeat > 1 we print a final
    median end-to-end across the runs.

.PARAMETER Cold
    If set, prompt the user to restart utterheim before the measurement so
    the first call is genuinely first-call-after-fresh-sidecar-boot.

.PARAMETER LogDir
    Directory holding utterheim-YYYYMMDD.log files. Default:
    %LOCALAPPDATA%\Utterheim\logs (matches EntryPoint.cs).

.EXAMPLE
    .\measure-latency.ps1 -InputFile .\short-input.txt -Cold

.EXAMPLE
    .\measure-latency.ps1 -InputFile .\medium-input.txt -Repeat 3
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputFile,

    [string]$Voice = 'alba',
    [string]$Endpoint = 'http://127.0.0.1:7223',
    [int]$Repeat = 1,
    [switch]$Cold,
    [string]$LogDir = "$env:LOCALAPPDATA\Utterheim\logs"
)

$ErrorActionPreference = 'Stop'

# --- Helpers -----------------------------------------------------------------

function Get-CurrentLogFile {
    param([string]$Dir)
    if (-not (Test-Path $Dir)) {
        throw "Log directory not found: $Dir"
    }
    $candidate = Get-ChildItem -Path $Dir -Filter 'utterheim-*.log' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "No utterheim-*.log found under $Dir. Is utterheim running?"
    }
    return $candidate.FullName
}

function Get-LogLineCount {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    # Use streaming line count; the log can be locked by Serilog. Open with
    # ReadShare so we don't fight the writer.
    $fs = $null
    $sr = $null
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
                                            [System.IO.FileAccess]::Read,
                                            [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $count = 0
        while ($null -ne $sr.ReadLine()) { $count++ }
        return $count
    }
    finally {
        if ($sr) { $sr.Dispose() }
        if ($fs) { $fs.Dispose() }
    }
}

function Find-FirstAudioDispatch {
    param(
        [string]$Path,
        [int]$SkipLines,
        [datetime]$NotBefore,
        [int]$TimeoutSeconds = 30
    )
    # Poll the log file every 100 ms looking for the first new line after
    # $SkipLines that contains "FIRST-AUDIO-DISPATCH" AND whose parsed local
    # timestamp is >= $NotBefore. Returns the parsed [datetime], or $null on timeout.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
                                                [System.IO.FileAccess]::Read,
                                                [System.IO.FileShare]::ReadWrite)
            $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
            try {
                $lineNo = 0
                while ($null -ne ($line = $sr.ReadLine())) {
                    $lineNo++
                    if ($lineNo -le $SkipLines) { continue }
                    if ($line -notmatch 'FIRST-AUDIO-DISPATCH') { continue }

                    # Serilog template: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [LVL] ..."
                    if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})') {
                        $ts = [datetime]::ParseExact(
                            $Matches[1], 'yyyy-MM-dd HH:mm:ss.fff',
                            [System.Globalization.CultureInfo]::InvariantCulture)
                        # Reject lines from before T0 (defensive — the line-count
                        # snapshot already filters most pre-existing lines).
                        if ($ts -ge $NotBefore.AddMilliseconds(-1)) {
                            return $ts
                        }
                    }
                }
            }
            finally {
                $sr.Dispose()
                $fs.Dispose()
            }
        }
        catch {
            # Log file briefly unreadable — keep polling.
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Invoke-OneMeasurement {
    param(
        [string]$Text,
        [string]$Voice,
        [string]$Endpoint,
        [string]$LogFile
    )

    # 1. Snapshot log line count before sending.
    $skipLines = Get-LogLineCount -Path $LogFile

    # 2. Build JSON body. The C# SpeakServer accepts {text, voice}.
    $bodyObj = @{ text = $Text; voice = $Voice }
    $bodyJson = $bodyObj | ConvertTo-Json -Compress
    $bodyTmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(),
        "mb-perf-$([guid]::NewGuid().ToString('N')).json")
    [System.IO.File]::WriteAllText($bodyTmp, $bodyJson, [System.Text.UTF8Encoding]::new($false))

    try {
        # 3. Capture T0 just before launching curl.exe. We use curl.exe (NOT the
        #    PowerShell `curl` alias which is Invoke-WebRequest with very different
        #    timing semantics).
        $curlExe = (Get-Command curl.exe -ErrorAction Stop).Source
        $writeOut = '%{time_starttransfer},%{time_total},%{http_code}'
        $t0 = Get-Date

        $curlArgs = @(
            '-sS',
            '-o', 'NUL',
            '-X', 'POST',
            "$Endpoint/speak",
            '-H', 'Content-Type: application/json',
            '--data-binary', "@$bodyTmp",
            '-w', $writeOut
        )
        $curlOutput = & $curlExe @curlArgs 2>&1
        $curlExit = $LASTEXITCODE
        if ($curlExit -ne 0) {
            throw "curl.exe failed (exit $curlExit): $curlOutput"
        }

        # 4. Parse curl -w line.
        $tStartTransfer = $null
        $tTotal = $null
        $httpCode = $null
        if ($curlOutput -match '([0-9.]+),([0-9.]+),(\d+)') {
            $tStartTransfer = [double]$Matches[1] * 1000.0
            $tTotal = [double]$Matches[2] * 1000.0
            $httpCode = [int]$Matches[3]
        }
        else {
            throw "Could not parse curl -w output: $curlOutput"
        }

        if ($httpCode -ne 200 -and $httpCode -ne 202 -and $httpCode -ne 204) {
            throw "Unexpected HTTP status: $httpCode"
        }

        # 5. Tail log for first FIRST-AUDIO-DISPATCH line after our snapshot.
        $firstAudioTs = Find-FirstAudioDispatch `
            -Path $LogFile -SkipLines $skipLines -NotBefore $t0 -TimeoutSeconds 30

        if ($null -eq $firstAudioTs) {
            return [pscustomobject]@{
                Success         = $false
                T0              = $t0
                TStartTransfer  = $tStartTransfer
                TTotal          = $tTotal
                EndToEnd        = $null
            }
        }

        $endToEnd = ($firstAudioTs - $t0).TotalMilliseconds
        return [pscustomobject]@{
            Success         = $true
            T0              = $t0
            TStartTransfer  = $tStartTransfer
            TTotal          = $tTotal
            EndToEnd        = $endToEnd
        }
    }
    finally {
        try { Remove-Item $bodyTmp -Force -ErrorAction SilentlyContinue } catch { }
    }
}

# --- Main --------------------------------------------------------------------

if (-not (Test-Path $InputFile)) {
    throw "InputFile not found: $InputFile"
}
$inputBase = [System.IO.Path]::GetFileName($InputFile)
$text = [System.IO.File]::ReadAllText($InputFile, [System.Text.Encoding]::UTF8).TrimEnd("`r", "`n", " ")
if ([string]::IsNullOrWhiteSpace($text)) {
    throw "InputFile is empty: $InputFile"
}

if ($Cold) {
    Write-Host ""
    Write-Host "[COLD] Restart utterheim now (Exit from tray, then relaunch)."
    Write-Host "       Wait until the tray status footer reads:"
    Write-Host "         HTTP $($Endpoint -replace '^https?://','') | Engine: running"
    Write-Host "       Then press Enter to begin the cold measurement..."
    [void](Read-Host)
}

$logFile = Get-CurrentLogFile -Dir $LogDir

$results = New-Object System.Collections.Generic.List[object]
for ($i = 1; $i -le $Repeat; $i++) {
    if ($i -gt 1) { Start-Sleep -Seconds 2 }

    $r = Invoke-OneMeasurement -Text $text -Voice $Voice -Endpoint $Endpoint -LogFile $logFile

    if (-not $r.Success) {
        Write-Host ("$inputBase | NO FIRST-AUDIO-DISPATCH within 30s - sidecar may have errored, check log") -ForegroundColor Red
        Write-Host ("  log file: $logFile")
        exit 1
    }

    $line = ('{0} | TTFB={1}ms | total={2}ms | end-to-end={3}ms' -f `
        $inputBase,
        [int][math]::Round($r.TStartTransfer),
        [int][math]::Round($r.TTotal),
        [int][math]::Round($r.EndToEnd))
    if ($Repeat -gt 1) { $line = "[run $i/$Repeat] $line" }
    Write-Host $line

    $results.Add($r) | Out-Null
}

if ($Repeat -gt 1) {
    $sorted = ($results | ForEach-Object { $_.EndToEnd } | Sort-Object)
    $median = if ($sorted.Count % 2 -eq 1) {
        $sorted[[int]([math]::Floor($sorted.Count / 2))]
    } else {
        ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
    }
    Write-Host ('median end-to-end: {0}ms' -f [int][math]::Round($median))
}
