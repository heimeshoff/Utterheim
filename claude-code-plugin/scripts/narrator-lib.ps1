# narrator-lib.ps1
#
# Pure, dependency-free helpers shared by the utterheim-narrator hooks.
# Dot-sourced by utterheim-speak.ps1 and by the Pester spec. No I/O at load
# time, no network. The only filesystem reads happen inside Resolve-Voice when
# it consults the per-repo voice-slot files (an explicit -BaseDir input).
#
# Exposes:
#   Get-NarratorLanguage  - classify spoken text as 'german' or 'english'
#   Resolve-Voice         - two-slot (EN/DE) voice resolution per ADR 0028
#   Test-VoiceMuted       - is a resolved voice the off/none/- mute marker?

# --- Language detection -----------------------------------------------------
#
# Offline EN/DE heuristic. Scores German signals against the final, already
# markdown-stripped spoken string; English is the safe default (8 built-in
# voices + the legacy convention are English). See ADR 0028 + the task spec.
#
#   germanScore = (anyUmlautOrEszett ? UmlautWeight : 0) + distinctStopwordHits
#   classify 'german' when germanScore >= Threshold, else 'english'
#
# UmlautWeight = 2, Threshold = 2  => umlaut alone OR >=2 distinct stopwords
# is enough; a single stopword (score 1) stays english (the documented tie /
# safe default). Text below a 2-word floor is always english.

$script:NarratorGermanStopwords = @(
    'der','die','das','den','dem','des','und','oder','nicht','ist','sind','war',
    'ich','du','er','sie','es','wir','ihr','mit','auch','noch','schon','eine',
    'einen','einem','eines','kein','keine','fuer','auf','aus','bei','nach',
    'unter','vom','zum','zur','dass','weil','aber','dann','wenn',
    # umlaut-bearing forms kept for completeness (also caught by the umlaut signal)
    'für','über'
)

function Get-NarratorLanguage {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text
    )

    $UmlautWeight = 2
    $Threshold    = 2
    $WordFloor    = 2

    if ([string]::IsNullOrWhiteSpace($Text)) { return 'english' }

    # Word-count floor: very short fragments ("ok") default to english.
    $words = [regex]::Matches($Text, '[\p{L}]+')
    if ($words.Count -lt $WordFloor) { return 'english' }

    $score = 0

    # Umlaut / ß signal.
    if ($Text -cmatch '[äöüÄÖÜß]') { $score += $UmlautWeight }

    # Distinct whole-word, case-insensitive German stopword hits.
    $hits = 0
    foreach ($sw in $script:NarratorGermanStopwords) {
        $pattern = '(?i)\b' + [regex]::Escape($sw) + '\b'
        if ([regex]::IsMatch($Text, $pattern)) { $hits++ }
    }
    $score += $hits

    if ($score -ge $Threshold) { return 'german' }
    return 'english'
}

# --- Voice-slot resolution (ADR 0028) ---------------------------------------

function Test-VoiceMuted {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Voice)
    if ([string]::IsNullOrWhiteSpace($Voice)) { return $false }
    return ($Voice -match '^(?i)(off|none|-)$')
}

function Get-SlotFileValue {
    param([string]$BaseDir, [string]$FileName)
    $path = Join-Path $BaseDir ".claude\$FileName"
    if (Test-Path -LiteralPath $path) {
        $v = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
        if ($v) {
            $v = $v.Trim()
            if (-not [string]::IsNullOrWhiteSpace($v)) { return $v }
        }
    }
    return $null
}

# Resolve the English/default slot:  file -> $env:UTTERHEIM_VOICE -> alba
function Resolve-EnglishVoice {
    param([string]$BaseDir)
    $v = Get-SlotFileValue -BaseDir $BaseDir -FileName 'utterheim-voice'
    if ($v) { return $v }
    if ($env:UTTERHEIM_VOICE) { return $env:UTTERHEIM_VOICE }
    return 'alba'
}

# Resolve a voice for the detected language, honoring an explicit override.
#
#   -Explicit  : when supplied, wins outright and bypasses detection/slots.
#   -Language  : 'german' or anything else (english/default/unknown).
#   -BaseDir   : repo root whose ./.claude/ slot files are consulted
#                (defaults to the current working directory for hook use).
#
# German chain: file utterheim-voice-de -> $env:UTTERHEIM_VOICE_DE
#               -> fall back to the resolved English/default slot
#               (NOT a hard-coded juergen).
function Resolve-Voice {
    [CmdletBinding()]
    param(
        [string]$Explicit,
        [string]$Language,
        [string]$BaseDir
    )

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit }
    if ([string]::IsNullOrWhiteSpace($BaseDir)) { $BaseDir = (Get-Location).Path }

    if ($Language -eq 'german') {
        $v = Get-SlotFileValue -BaseDir $BaseDir -FileName 'utterheim-voice-de'
        if ($v) { return $v }
        if ($env:UTTERHEIM_VOICE_DE) { return $env:UTTERHEIM_VOICE_DE }
        # No German slot configured: keep the session's chosen identity.
        return (Resolve-EnglishVoice -BaseDir $BaseDir)
    }

    return (Resolve-EnglishVoice -BaseDir $BaseDir)
}
