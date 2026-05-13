# utterheim-stop.ps1
#
# Claude Code Stop hook: reads the hook payload on stdin, finds the last
# assistant text message in the session transcript, and forwards it to
# utterheim-speak.ps1 so it gets spoken aloud.
#
# Honors the global sound toggle: ~/.utterheim/sound-disabled mutes all
# TTS output across every repo on the machine.
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
    $transcriptPath = $payload.transcript_path
    if ([string]::IsNullOrWhiteSpace($transcriptPath) -or -not (Test-Path -LiteralPath $transcriptPath)) {
        exit 0
    }

    $lines = Get-Content -LiteralPath $transcriptPath -ErrorAction SilentlyContinue
    if (-not $lines -or $lines.Count -eq 0) { exit 0 }

    $summary = $null
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        try {
            $entry = $line | ConvertFrom-Json -ErrorAction Stop
        } catch { continue }

        if ($entry.type -ne 'assistant') { continue }
        if (-not $entry.message -or -not $entry.message.content) { continue }

        $textParts = @()
        foreach ($block in $entry.message.content) {
            if ($block.type -eq 'text' -and -not [string]::IsNullOrWhiteSpace($block.text)) {
                $textParts += $block.text
            }
        }
        if ($textParts.Count -gt 0) {
            $summary = ($textParts -join "`n").Trim()
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($summary)) { exit 0 }

    # Strip markdown so TTS doesn't read punctuation as words.
    $summary = $summary -replace '```[\s\S]*?```', ' '
    $summary = $summary -replace '`([^`]*)`', '$1'
    $summary = $summary -replace '\*\*([^*]+)\*\*', '$1'
    $summary = $summary -replace '(?<!\*)\*([^*]+)\*(?!\*)', '$1'
    $summary = $summary -replace '\[([^\]]+)\]\([^)]+\)', '$1'
    $summary = $summary -replace '^\s*#{1,6}\s*', '' -replace "`n\s*#{1,6}\s*", "`n"
    $summary = ($summary -replace '\s+', ' ').Trim()

    if ([string]::IsNullOrWhiteSpace($summary)) { exit 0 }

    if ($summary.Length -gt 1000) {
        $summary = $summary.Substring(0, 1000)
    }

    $speak = Join-Path $PSScriptRoot 'utterheim-speak.ps1'
    & $speak -Text $summary -Silent | Out-Null
    exit 0
}
catch {
    exit 0
}
