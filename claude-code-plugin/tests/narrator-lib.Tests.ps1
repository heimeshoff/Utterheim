# narrator-lib.Tests.ps1
#
# Pester spec for the narrator plugin's pure functions:
#   Get-NarratorLanguage  - offline EN/DE language detection heuristic
#   Resolve-Voice         - two-slot (EN/DE) voice resolution per ADR 0028
#
# Compatible with Pester 3.x (bundled with Windows PowerShell 5.1) and Pester 5.x.
# Run:  Invoke-Pester -Path .\tests\narrator-lib.Tests.ps1

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\scripts\narrator-lib.ps1')

Describe 'Get-NarratorLanguage' {

    Context 'German signals' {
        It 'classifies umlaut/ß text as german' {
            Get-NarratorLanguage 'Die Änderung ist fertig' | Should Be 'german'
            Get-NarratorLanguage 'Der Test läuft durch' | Should Be 'german'
            Get-NarratorLanguage 'Die Straße ist gesperrt' | Should Be 'german'
        }

        It 'classifies text with >=2 distinct German stopwords (no umlaut) as german' {
            Get-NarratorLanguage 'ich habe das nicht gemacht' | Should Be 'german'
            Get-NarratorLanguage 'der test ist gut und schnell' | Should Be 'german'
        }
    }

    Context 'English / safe defaults' {
        It 'classifies plain English prose as english' {
            Get-NarratorLanguage 'I finished the refactor and all tests pass' | Should Be 'english'
        }

        It 'does not false-positive on code-heavy / identifier-heavy English' {
            Get-NarratorLanguage 'const userId = getUser(id).der_field // order_state IST_READY' | Should Be 'english'
            Get-NarratorLanguage 'Refactored OrderService.dispatch to use die-cast diet constants' | Should Be 'english'
        }

        It 'classifies empty, whitespace-only, and sub-floor text as english' {
            Get-NarratorLanguage '' | Should Be 'english'
            Get-NarratorLanguage '   ' | Should Be 'english'
            Get-NarratorLanguage $null | Should Be 'english'
            Get-NarratorLanguage 'ok' | Should Be 'english'
        }

        It 'classifies a genuine tie as english (documented default)' {
            # exactly one German stopword, no umlaut -> score 1 -> below threshold -> english
            Get-NarratorLanguage 'this is der plan for today' | Should Be 'english'
        }

        It 'matches stopwords whole-word and case-insensitively only' {
            # "diet" / "order" / "IST-state" must NOT count die/der/ist as German hits
            Get-NarratorLanguage 'the diet plan and the order queue and IST state pipeline' | Should Be 'english'
        }
    }

    Context 'Purity' {
        It 'is a pure function (same input -> same output)' {
            $a = Get-NarratorLanguage 'Die Änderung ist fertig und der Test läuft'
            $b = Get-NarratorLanguage 'Die Änderung ist fertig und der Test läuft'
            $a | Should Be $b
            $a | Should Be 'german'
        }

        It 'is case-insensitive for stopword scoring' {
            Get-NarratorLanguage 'ICH HABE DAS NICHT GEMACHT' | Should Be 'german'
        }
    }
}

Describe 'Resolve-Voice (two-slot, ADR 0028)' {

    BeforeEach {
        $script:repo = Join-Path ([System.IO.Path]::GetTempPath()) ("narr-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $script:repo '.claude') -Force | Out-Null
        # clean slate for env mirrors
        Remove-Item Env:UTTERHEIM_VOICE -ErrorAction SilentlyContinue
        Remove-Item Env:UTTERHEIM_VOICE_DE -ErrorAction SilentlyContinue
    }

    AfterEach {
        Remove-Item -LiteralPath $script:repo -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item Env:UTTERHEIM_VOICE -ErrorAction SilentlyContinue
        Remove-Item Env:UTTERHEIM_VOICE_DE -ErrorAction SilentlyContinue
    }

    function Set-Slot([string]$name, [string]$value) {
        Set-Content -LiteralPath (Join-Path $script:repo ".claude\$name") -Value $value -Encoding utf8 -NoNewline
    }

    It 'english slot file wins: utterheim-voice=marius -> marius' {
        Set-Slot 'utterheim-voice' 'marius'
        Resolve-Voice -Language 'english' -BaseDir $script:repo | Should Be 'marius'
    }

    It 'german slot file: utterheim-voice-de=juergen -> juergen' {
        Set-Slot 'utterheim-voice' 'marius'
        Set-Slot 'utterheim-voice-de' 'juergen'
        Resolve-Voice -Language 'german' -BaseDir $script:repo | Should Be 'juergen'
    }

    It 'german with no DE slot and no env falls back to resolved english voice (not hard-coded juergen)' {
        Set-Slot 'utterheim-voice' 'marius'
        Resolve-Voice -Language 'german' -BaseDir $script:repo | Should Be 'marius'
    }

    It 'german with no DE slot and english slot=alba (default) -> alba' {
        # no files at all -> english resolves to alba default -> german falls through
        Resolve-Voice -Language 'german' -BaseDir $script:repo | Should Be 'alba'
    }

    It 'honors $env:UTTERHEIM_VOICE_DE for the German slot when file absent' {
        Set-Slot 'utterheim-voice' 'marius'
        $env:UTTERHEIM_VOICE_DE = 'juergen'
        Resolve-Voice -Language 'german' -BaseDir $script:repo | Should Be 'juergen'
    }

    It 'DE file wins over $env:UTTERHEIM_VOICE_DE when both present' {
        Set-Slot 'utterheim-voice-de' 'juergen'
        $env:UTTERHEIM_VOICE_DE = 'someone-else'
        Resolve-Voice -Language 'german' -BaseDir $script:repo | Should Be 'juergen'
    }

    It 'legacy english resolution: file > env > alba default' {
        Resolve-Voice -Language 'english' -BaseDir $script:repo | Should Be 'alba'
        $env:UTTERHEIM_VOICE = 'fantine'
        Resolve-Voice -Language 'english' -BaseDir $script:repo | Should Be 'fantine'
        Set-Slot 'utterheim-voice' 'marius'
        Resolve-Voice -Language 'english' -BaseDir $script:repo | Should Be 'marius'
    }

    It 'explicit -Voice overrides both slots and bypasses detection' {
        Set-Slot 'utterheim-voice' 'marius'
        Set-Slot 'utterheim-voice-de' 'juergen'
        Resolve-Voice -Explicit 'javert' -Language 'german' -BaseDir $script:repo | Should Be 'javert'
        Resolve-Voice -Explicit 'javert' -Language 'english' -BaseDir $script:repo | Should Be 'javert'
    }
}

Describe 'Mute semantics on resolved voice (Test-VoiceMuted)' {

    It 'recognizes off/none/- markers (case-insensitive) as muted' {
        Test-VoiceMuted 'off'  | Should Be $true
        Test-VoiceMuted 'none' | Should Be $true
        Test-VoiceMuted '-'    | Should Be $true
        Test-VoiceMuted 'OFF'  | Should Be $true
    }

    It 'treats real voice ids as not muted' {
        Test-VoiceMuted 'marius'  | Should Be $false
        Test-VoiceMuted 'juergen' | Should Be $false
        Test-VoiceMuted 'alba'    | Should Be $false
    }

    It 'fully muted repo: english slot=off, no DE slot -> german resolves to off -> muted' {
        $repo = Join-Path ([System.IO.Path]::GetTempPath()) ("narr-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $repo '.claude') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $repo '.claude\utterheim-voice') -Value 'off' -Encoding utf8 -NoNewline
        Remove-Item Env:UTTERHEIM_VOICE_DE -ErrorAction SilentlyContinue
        try {
            (Test-VoiceMuted (Resolve-Voice -Language 'english' -BaseDir $repo)) | Should Be $true
            (Test-VoiceMuted (Resolve-Voice -Language 'german'  -BaseDir $repo)) | Should Be $true
        } finally {
            Remove-Item -LiteralPath $repo -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'english off but real DE voice: english muted, german speaks' {
        $repo = Join-Path ([System.IO.Path]::GetTempPath()) ("narr-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $repo '.claude') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $repo '.claude\utterheim-voice') -Value 'off' -Encoding utf8 -NoNewline
        Set-Content -LiteralPath (Join-Path $repo '.claude\utterheim-voice-de') -Value 'juergen' -Encoding utf8 -NoNewline
        Remove-Item Env:UTTERHEIM_VOICE_DE -ErrorAction SilentlyContinue
        try {
            (Test-VoiceMuted (Resolve-Voice -Language 'english' -BaseDir $repo)) | Should Be $true
            (Test-VoiceMuted (Resolve-Voice -Language 'german'  -BaseDir $repo)) | Should Be $false
            (Resolve-Voice -Language 'german' -BaseDir $repo) | Should Be 'juergen'
        } finally {
            Remove-Item -LiteralPath $repo -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
