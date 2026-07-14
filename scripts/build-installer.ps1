#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes the EXE and compiles the Inno Setup installer.

.DESCRIPTION
  Version comes from Directory.Build.props (single source of truth).
  Bump with: .\scripts\bump-version.ps1
  Then build: .\scripts\build-installer.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -InstallPrerequisites
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -BumpPatch
#>
param(
    [switch] $SkipPublish,
    [switch] $InstallPrerequisites,
    [switch] $BumpPatch
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptsDir = $PSScriptRoot

if ($BumpPatch) {
    & (Join-Path $scriptsDir 'bump-version.ps1') -Part patch
}

if ($InstallPrerequisites) {
    & (Join-Path $scriptsDir 'install-build-prerequisites.ps1')
}

if (-not $SkipPublish) {
    & (Join-Path $scriptsDir 'publish-installer.ps1')
}
else {
    $exe = Join-Path $repoRoot 'src\LocalMusicHub\bin\Publish\win-x64\LocalMusicHub.exe'
    if (-not (Test-Path $exe)) { throw "EXE not found: $exe - run without -SkipPublish first." }
    Write-Host "SkipPublish: using existing $exe"
}

function Find-IsccPath {
    $roots = @(
        $env:INNO_SETUP_ROOT
        "$env:LOCALAPPDATA\Programs\Inno Setup 6"
        "${env:ProgramFiles(x86)}\Inno Setup 6"
        "$env:ProgramFiles\Inno Setup 6"
    ) | Where-Object { $_ -and (Test-Path $_) }
    foreach ($r in $roots) {
        $p = Join-Path $r 'ISCC.exe'
        if (Test-Path $p) { return $p }
    }
    return $null
}

$iscc = Find-IsccPath
if (-not $iscc) {
    throw "Inno Setup 6 not found. Run scripts\install-build-prerequisites.ps1 or install from https://jrsoftware.org/isdl.php"
}

$appVersion = & (Join-Path $scriptsDir 'get-version.ps1')
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Could not read Version from Directory.Build.props (get-version.ps1 returned empty)."
}
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid Version '$appVersion' from Directory.Build.props (expected major.minor.patch)."
}

& (Join-Path $scriptsDir 'write-version-inc.ps1') -Version $appVersion | Out-Null

$issDir = Join-Path $repoRoot 'installer'
$setupName = "LocalMusicHub-Setup-$appVersion.exe"
$outFile = Join-Path $issDir "Output\$setupName"

Write-Host ""
Write-Host "Building installer $setupName"
Write-Host "  Version source: Directory.Build.props"
Write-Host "  ISCC: $iscc"

Push-Location $issDir
try {
    & $iscc "/DAppVersion=$appVersion" ".\LocalMusicHub.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

if (-not (Test-Path $outFile)) {
    throw "Expected installer was not created: $outFile`nCheck ISCC output above - AppVersion may not have been applied."
}

Write-Host ""
Write-Host "Done. Installer:"
Write-Host "  $outFile"
Write-Host "  Size: $([math]::Round((Get-Item $outFile).Length / 1MB, 1)) MB"
