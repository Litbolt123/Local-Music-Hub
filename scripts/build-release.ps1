#Requires -Version 5.1
<#
.SYNOPSIS
  Release build + sync to Programs folder + update Windows shortcuts.
#>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\LocalMusicHub\LocalMusicHub.csproj'

Write-Host 'Building Local Music Hub (Release)...'
& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
Write-Host 'Release build complete (shortcuts updated via post-build step).'
