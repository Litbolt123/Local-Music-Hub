#Requires -Version 5.1
<#
.SYNOPSIS
  Installs .NET 8 SDK and Inno Setup 6 for building the app and installer.
#>
$ErrorActionPreference = 'Stop'

function Refresh-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Test-DotNet8Sdk {
    try {
        foreach ($line in (& dotnet --list-sdks 2>&1)) {
            if ($line -match '^8\.') { return $true }
        }
    } catch { }
    return $false
}

function Test-InnoSetup {
    $roots = @($env:INNO_SETUP_ROOT, "$env:LOCALAPPDATA\Programs\Inno Setup 6", "${env:ProgramFiles(x86)}\Inno Setup 6", "$env:ProgramFiles\Inno Setup 6") |
        Where-Object { $_ -and (Test-Path $_) }
    foreach ($r in $roots) {
        if (Test-Path (Join-Path $r 'ISCC.exe')) { return $true }
    }
    return $false
}

function Install-WingetId([string]$Id, [string]$Label) {
    Write-Host "Installing $Label via winget ($Id)..."
    & winget install --id $Id -e --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw "winget install $Id failed" }
}

$needDotNet = -not (Test-DotNet8Sdk)
$needInno = -not (Test-InnoSetup)
if (-not $needDotNet -and -not $needInno) {
    Write-Host ".NET 8 SDK and Inno Setup 6 already installed."
    exit 0
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) { throw "winget not found — install .NET 8 SDK and Inno Setup 6 manually." }

if ($needDotNet) {
    Install-WingetId 'Microsoft.DotNet.SDK.8' '.NET 8 SDK'
    Refresh-SessionPath
}
if ($needInno) {
    Install-WingetId 'JRSoftware.InnoSetup' 'Inno Setup 6'
    Refresh-SessionPath
}

Write-Host "Done. Run scripts\build-installer.ps1 next."
