# Developer setup for Windows: installs Git (for Git Bash), the .NET 10 SDK,
# just and Python via winget when missing, writes .env and server.json, builds.
# To only run the server, use run.ps1 (Docker only) instead.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Install-IfMissing([string]$Command, [string]$WingetId) {
    if (Get-Command $Command -ErrorAction SilentlyContinue) { Write-Host "found $Command"; return $false }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "$Command is missing and winget is not available; install $WingetId by hand" }
    winget install --id $WingetId -e --accept-package-agreements --accept-source-agreements
    return $true
}

$installed = $false
$installed = (Install-IfMissing git Git.Git) -or $installed
$installed = (Install-IfMissing python Python.Python.3.12) -or $installed
$installed = (Install-IfMissing just Casey.Just) -or $installed
$hasSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match '^10\.')
if ($hasSdk) { Write-Host "found .NET $(dotnet --version)" }
else { winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements; $installed = $true }

if ($installed) {
    Write-Host "`nTools were installed. Open a NEW terminal (so PATH refreshes) and run .\setup.ps1 again."
    exit 0
}

$env:UBIQUITOUS_DEV_SETUP = '1'
& (Join-Path $PSScriptRoot 'scripts\setup\init-config.ps1')
Set-Location $PSScriptRoot
dotnet build -v quiet --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

Write-Host @'

Ready. Next, in Git Bash or PowerShell:
  1. copy your-client.ipa incoming\
  2. just ingest
  3. just dev
The server listens on ws://127.0.0.1:8781/.
'@
