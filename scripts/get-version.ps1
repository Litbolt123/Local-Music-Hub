#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) { throw "Directory.Build.props not found: $propsPath" }

$propsContent = Get-Content $propsPath -Raw
$m = [regex]::Match($propsContent, '<Version>\s*([^<]+?)\s*</Version>')
if (-not $m.Success) { throw "No <Version> in Directory.Build.props" }

$v = $m.Groups[1].Value.Trim()
if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid <Version> in Directory.Build.props: '$v'" }
Write-Output $v
