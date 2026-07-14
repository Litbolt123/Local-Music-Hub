#Requires -Version 5.1
<#
.SYNOPSIS
  Bumps Version in Directory.Build.props (patch by default).

.EXAMPLE
  .\scripts\bump-version.ps1
  .\scripts\bump-version.ps1 -Part minor
#>
param(
    [ValidateSet('patch', 'minor', 'major')]
    [string] $Part = 'patch'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) { throw "Not found: $propsPath" }

$content = Get-Content $propsPath -Raw
$m = [regex]::Match($content, '<Version>(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $m.Success) { throw "Could not parse <Version> in Directory.Build.props" }

$major = [int]$m.Groups[1].Value
$minor = [int]$m.Groups[2].Value
$patch = [int]$m.Groups[3].Value

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}

$newVersion = "$major.$minor.$patch"
$newFour = "$newVersion.0"

$content = [regex]::Replace($content, '<Version>.*?</Version>', "<Version>$newVersion</Version>")
$content = [regex]::Replace($content, '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$newFour</AssemblyVersion>")
$content = [regex]::Replace($content, '<FileVersion>.*?</FileVersion>', "<FileVersion>$newFour</FileVersion>")
Set-Content -Path $propsPath -Value ($content.TrimEnd() + "`n") -Encoding UTF8 -NoNewline

& (Join-Path $PSScriptRoot 'write-version-inc.ps1') -Version $newVersion | Out-Null

Write-Host "Bumped Directory.Build.props to $newVersion"
Write-Host "Next: powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1"
