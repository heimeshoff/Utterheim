# mockingbird-stop-hook.ps1
#
# Claude Code Stop hook: reads the hook payload on stdin, finds the last
# assistant text message in the session transcript, and forwards it to
# mockingbird-hook.ps1 so it gets spoken aloud.
#
# This replaces the previous fixed "task done" announcement — Claude's actual
# end-of-turn summary is read instead.
#
# Voice routing: same env-var contract as mockingbird-hook.ps1 — set
# MOCKINGBIRD_VOICE in the calling shell (or via Claude Code's settings.json
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
    $transcriptPath = $payload.transcript_path
    if ([string]::IsNullOrWhiteSpace($transcriptPath) -or -not (Test-Path -LiteralPath $transcriptPath)) {
        exit 0
    }

    # Walk transcript lines from the end; pick the last assistant message
    # with non-empty text content. Tool-only turns (no text blocks) are skipped.
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

    # Light markdown cleanup so TTS doesn't read punctuation as words.
    $summary = $summary -replace '```[\s\S]*?```', ' '   # fenced code blocks
    $summary = $summary -replace '`([^`]*)`', '$1'        # inline code
    $summary = $summary -replace '\*\*([^*]+)\*\*', '$1'  # bold
    $summary = $summary -replace '(?<!\*)\*([^*]+)\*(?!\*)', '$1'  # italic
    $summary = $summary -replace '\[([^\]]+)\]\([^)]+\)', '$1'     # links
    $summary = $summary -replace '^\s*#{1,6}\s*', '' -replace "`n\s*#{1,6}\s*", "`n"  # headers
    $summary = ($summary -replace '\s+', ' ').Trim()

    if ([string]::IsNullOrWhiteSpace($summary)) { exit 0 }

    # Cap absurdly long summaries to keep the spoken output bounded.
    if ($summary.Length -gt 1000) {
        $summary = $summary.Substring(0, 1000)
    }

    $hookScript = Join-Path $PSScriptRoot 'mockingbird-hook.ps1'
    & $hookScript -Text $summary -Silent | Out-Null
    exit 0
}
catch {
    # Never block Claude Code on a TTS error.
    exit 0
}
