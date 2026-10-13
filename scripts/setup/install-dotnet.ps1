$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$channel    = '10.0'

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks -match [regex]::Escape($channel)) {
        Write-Host ".NET $channel SDK already present:"
        $sdks | ForEach-Object { Write-Host "  $_" }
        exit 0
    }
}

$scriptPath = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath -UseBasicParsing
& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Channel $channel -InstallDir $installDir

Write-Host ''
Write-Host "Installed to $installDir"
Write-Host 'Add it to PATH for new shells:'
Write-Host "  [Environment]::SetEnvironmentVariable('Path', '$installDir;' + [Environment]::GetEnvironmentVariable('Path','User'), 'User')"
