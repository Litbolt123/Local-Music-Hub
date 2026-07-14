#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src\LocalMusicHub'
$publishDir = Join-Path $projectDir 'bin\Publish\win-x64'

$iconPath = Join-Path $projectDir 'app.ico'
if (-not (Test-Path $iconPath)) {
    Write-Host "Generating app.ico..."
    & (Join-Path $repoRoot 'scripts\make-app-icon.ps1')
}

if (Test-Path $publishDir) {
    Write-Host "Cleaning $publishDir"
    Remove-Item $publishDir -Recurse -Force
}

Push-Location $projectDir
try {
    & dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$ver = & (Join-Path $repoRoot 'scripts\get-version.ps1')
Write-Host "Published (installer build) to: $publishDir (Version $ver)"
& (Join-Path $repoRoot 'scripts\update-windows-shortcuts.ps1') -SourceDir $publishDir
