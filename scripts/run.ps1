#Requires -Version 5.1
<#
.SYNOPSIS
  Builds (if needed) and launches Local Music Hub. Prefer this over elevated CMD + raw dotnet run.
#>
param(
    [switch] $NoBuild,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\LocalMusicHub\LocalMusicHub.csproj'
$exe = Join-Path $repoRoot "src\LocalMusicHub\bin\$Configuration\net8.0-windows10.0.19041.0\LocalMusicHub.exe"

if (-not $NoBuild -or -not (Test-Path $exe)) {
    Write-Host "Building ($Configuration)..."
    & dotnet build $project -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
}
elseif ($Configuration -eq 'Release') {
    & (Join-Path $PSScriptRoot 'update-windows-shortcuts.ps1') -SourceDir (Split-Path $exe)
}

if (-not (Test-Path $exe)) { throw "EXE not found: $exe" }

Write-Host "Starting $exe"
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
