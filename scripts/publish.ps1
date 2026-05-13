<#
.SYNOPSIS
    Stop, publish, and relaunch utterheim.

.DESCRIPTION
    Full local deploy cycle:
      1. Stop any running utterheim.exe (releases the file lock so publish
         can overwrite it).
      2. Publish both projects in Release / win-x64:
           - src\Utterheim\Utterheim.csproj      -> utterheim.exe (WPF tray host)
           - src\Utterheim.Cli\Utterheim.Cli.csproj -> utterheim-speak.exe (CLI)
      3. Stage outputs into .\dist\.
      4. Launch the freshly published utterheim.exe (detached).

    The tray app is published as a single-file, self-contained executable so it
    runs on machines without the .NET 9 desktop runtime installed. The CLI csproj
    already declares PublishSingleFile/SelfContained.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release.

.PARAMETER FrameworkDependent
    Skip self-contained bundling — produces a small exe that requires .NET 9 on
    the target machine. Off by default.

.PARAMETER NoLaunch
    Skip step 4 (do not start the new exe after publish).

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -FrameworkDependent
    .\scripts\publish.ps1 -NoLaunch
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$FrameworkDependent,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$trayProj = Join-Path $repoRoot 'src\Utterheim\Utterheim.csproj'
$cliProj  = Join-Path $repoRoot 'src\Utterheim.Cli\Utterheim.Cli.csproj'
$distDir  = Join-Path $repoRoot 'dist'

$selfContained = -not $FrameworkDependent

# 1. Stop running tray instance(s) so the exe isn't locked. AssemblyName is
#    "utterheim" (lowercase) per Utterheim.csproj.
$running = Get-Process -Name 'utterheim' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running utterheim process(es)..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    # Brief pause so Windows actually releases the file handle before publish writes to it.
    Start-Sleep -Milliseconds 500
}

Write-Host "Publishing utterheim ($Configuration, win-x64, self-contained=$selfContained)" -ForegroundColor Cyan

# 2a. Tray app — flags supplied here because the csproj does not pin them.
$trayArgs = @(
    'publish', $trayProj,
    '-c', $Configuration,
    '-r', 'win-x64',
    "-p:PublishSingleFile=true",
    "-p:SelfContained=$selfContained",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)
& dotnet @trayArgs
if ($LASTEXITCODE -ne 0) { throw "tray publish failed (exit $LASTEXITCODE)" }

# 2b. CLI — csproj already sets PublishSingleFile + SelfContained.
$cliArgs = @(
    'publish', $cliProj,
    '-c', $Configuration,
    '-r', 'win-x64'
)
if ($FrameworkDependent) { $cliArgs += '-p:SelfContained=false' }
& dotnet @cliArgs
if ($LASTEXITCODE -ne 0) { throw "cli publish failed (exit $LASTEXITCODE)" }

# 3. Stage outputs in dist\ so the user has one place to grab artifacts from.
$trayPublish = Join-Path $repoRoot "src\Utterheim\bin\$Configuration\net9.0-windows\win-x64\publish"
$cliPublish  = Join-Path $repoRoot "src\Utterheim.Cli\bin\$Configuration\net9.0\win-x64\publish"

if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $distDir 'tray') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $distDir 'cli')  | Out-Null

Copy-Item -Path (Join-Path $trayPublish '*') -Destination (Join-Path $distDir 'tray') -Recurse -Force
Copy-Item -Path (Join-Path $cliPublish  '*') -Destination (Join-Path $distDir 'cli')  -Recurse -Force

$trayExe = Join-Path $distDir 'tray\utterheim.exe'
$cliExe  = Join-Path $distDir 'cli\utterheim-speak.exe'

Write-Host ""
Write-Host "Published." -ForegroundColor Green
Write-Host "  Tray: $trayExe"
Write-Host "  CLI : $cliExe"

# 4. Launch the new tray exe detached so this script returns immediately.
if (-not $NoLaunch) {
    Write-Host ""
    Write-Host "Launching $trayExe ..." -ForegroundColor Cyan
    Start-Process -FilePath $trayExe -WorkingDirectory (Split-Path $trayExe)
}
