#Requires -Version 5.1
param(
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$issDir = Join-Path $repoRoot 'installer'
if (-not (Test-Path $issDir)) { New-Item -ItemType Directory -Path $issDir | Out-Null }
$incPath = Join-Path $issDir 'version.inc'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = & (Join-Path $PSScriptRoot 'get-version.ps1')
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version' (expected major.minor.patch)"
}

$lines = @(
    '; Auto-generated — edit Directory.Build.props, then run build-installer.ps1 or bump-version.ps1.'
    "#define AppVersion `"$Version`""
)
Set-Content -Path $incPath -Value (($lines -join "`r`n") + "`r`n") -Encoding ASCII -NoNewline
Write-Output $Version
