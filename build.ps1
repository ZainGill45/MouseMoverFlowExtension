<#
  Build the Mouse Mover plugin and deploy it into Flow Launcher for local testing.
  Usage:  pwsh ./build.ps1            (build + deploy)
          pwsh ./build.ps1 -NoDeploy  (build only)

  Requires the .NET SDK (9 or newer) on Windows (WPF settings panel needs Windows).
#>
param([switch]$NoDeploy)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'bin\plugin'

dotnet build -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if ($NoDeploy) { return }

# Folder name tracks the manifest version so deploys never land in a stale folder.
$version = (Get-Content (Join-Path $root 'plugin.json') -Raw | ConvertFrom-Json).Version
$dest = "$env:APPDATA\FlowLauncher\Plugins\MouseMover-$version"
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\*" $dest -Recurse -Force
Write-Host "Deployed to: $dest" -ForegroundColor Green
Write-Host "Restart Flow Launcher (or run its 'Restart' command) to load changes." -ForegroundColor Yellow
