#Requires -Version 5.1
<#
.SYNOPSIS
  Syncs a Release build to %LocalAppData%\Programs and updates Start Menu / Desktop shortcuts.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\update-windows-shortcuts.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\update-windows-shortcuts.ps1 -SourceDir .\src\LocalMusicHub\bin\Release\net8.0-windows10.0.19041.0
#>
param(
    [string] $SourceDir,
    [switch] $SkipProcessStop
)

$ErrorActionPreference = 'Stop'

$AppDisplayName = 'Local Music Hub'
$ProcessName = 'LocalMusicHub'
$ExeName = 'LocalMusicHub.exe'
$InstallFolderName = 'LocalMusicHub'
$ShortcutNames = @('Local Music Hub')
$ProjectSubdir = 'src\LocalMusicHub'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot $ProjectSubdir
$iconPath = Join-Path $projectDir 'app.ico'
$releaseDir = Join-Path $projectDir 'bin\Release\net8.0-windows10.0.19041.0'
$publishDir = Join-Path $projectDir 'bin\Publish\win-x64'

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    if (Test-Path (Join-Path $publishDir $ExeName)) {
        $SourceDir = $publishDir
    }
    else {
        $SourceDir = $releaseDir
    }
}

$SourceDir = $SourceDir.Trim().TrimEnd('\', '/')
if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path
}
$sourceExe = Join-Path $SourceDir $ExeName
if (-not (Test-Path $sourceExe)) {
    throw "Build output not found: $sourceExe"
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\$InstallFolderName"
$installExe = Join-Path $installDir $ExeName
$version = & (Join-Path $repoRoot 'scripts\get-version.ps1')

function Stop-LocalMusicHubGracefully {
    if ($SkipProcessStop) { return }

    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if (-not $running) { return }

    $requestPath = Join-Path $env:LOCALAPPDATA "$InstallFolderName\shutdown.request"
    $dataDir = Split-Path $requestPath -Parent
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    Set-Content -Path $requestPath -Value ((Get-Date).ToUniversalTime().ToString('o')) -Encoding UTF8

    $deadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
}

Stop-LocalMusicHubGracefully

Write-Host "Syncing $AppDisplayName $version"
Write-Host "  From: $SourceDir"
Write-Host "  To:   $installDir"

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
& robocopy $SourceDir $installDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "robocopy failed with exit code $LASTEXITCODE"
}

if (Test-Path $iconPath) {
    Copy-Item $iconPath (Join-Path $installDir 'app.ico') -Force
}

function Update-Shortcut {
    param(
        [Parameter(Mandatory)][string] $ShortcutPath,
        [Parameter(Mandatory)][string] $TargetExe,
        [Parameter(Mandatory)][string] $WorkingDir,
        [string] $IconPath,
        [string] $Description
    )

    $parent = Split-Path $ShortcutPath -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetExe
    $shortcut.WorkingDirectory = $WorkingDir
    if ($IconPath -and (Test-Path $IconPath)) {
        $shortcut.IconLocation = "$IconPath,0"
    }
    if ($Description) {
        $shortcut.Description = $Description
    }
    $shortcut.Save()
}

$description = "$AppDisplayName $version"
$iconForShortcut = if (Test-Path (Join-Path $installDir 'app.ico')) { Join-Path $installDir 'app.ico' } else { $iconPath }

$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$primaryStartLnk = Join-Path $startMenuDir "$AppDisplayName.lnk"
Update-Shortcut -ShortcutPath $primaryStartLnk -TargetExe $installExe -WorkingDir $installDir `
    -IconPath $iconForShortcut -Description $description
Write-Host "  Start Menu: $primaryStartLnk"

$shortcutRoots = @(
    $startMenuDir
    [Environment]::GetFolderPath('Desktop')
    [Environment]::GetFolderPath('CommonDesktopDirectory')
)

$shell = New-Object -ComObject WScript.Shell
$updated = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($root in $shortcutRoots) {
    if (-not (Test-Path $root)) { continue }

    Get-ChildItem -Path $root -Filter '*.lnk' -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($updated.Contains($_.FullName)) { return }

        $shouldUpdate = $ShortcutNames -contains $_.BaseName
        if (-not $shouldUpdate) {
            try {
                $target = $shell.CreateShortcut($_.FullName).TargetPath
                if ($target -and ($target -like "*\$InstallFolderName\$ExeName" -or $target -like "*\$ExeName")) {
                    $shouldUpdate = $true
                }
            }
            catch {
                return
            }
        }

        if ($shouldUpdate) {
            Update-Shortcut -ShortcutPath $_.FullName -TargetExe $installExe -WorkingDir $installDir `
                -IconPath $iconForShortcut -Description $description
            $updated.Add($_.FullName) | Out-Null
            Write-Host "  Shortcut: $($_.FullName)"
        }
    }
}

Write-Host "Done. Shortcuts now launch: $installExe"

# robocopy uses 0-7 for success; a code of 1+ is normal after copy. Normalize so callers/CI do not fail.
if ($LASTEXITCODE -gt 7) { exit $LASTEXITCODE }
exit 0
