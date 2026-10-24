# Writes .env (generated secrets) and server.json when missing, and creates the
# folders the other scripts expect. Shared by setup.ps1 and run.ps1; safe to re-run.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..\..')

function New-Secret([int]$Length) {
    $chars = [char[]]'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    -join (1..$Length | ForEach-Object { $chars[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($chars.Length)] })
}

foreach ($dir in 'incoming', 'data\generated', 'data\cache') { New-Item -ItemType Directory -Force $dir | Out-Null }

if (-not (Test-Path .env)) {
    $dbPass = New-Secret 24
    $text = (Get-Content env.example -Raw) -replace '(?m)^POSTGRES_PASSWORD=.*', "POSTGRES_PASSWORD=$dbPass"
    # Development logins are for `just dev` only (the server ignores them with Postgres).
    if ($env:UBIQUITOUS_DEV_SETUP) {
        $devPass = New-Secret 16
        $text = $text `
            -replace '(?m)^UBIQUITOUS_DEVELOPMENT_ACCOUNTS=.*', 'UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true' `
            -replace '(?m)^UBIQUITOUS_DEVELOPMENT_PASS_HASH=.*', "UBIQUITOUS_DEVELOPMENT_PASS_HASH=$devPass"
    }
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) '.env'), $text)
    Write-Host 'wrote .env with generated secrets'
    if ($env:UBIQUITOUS_DEV_SETUP) { Write-Host "  test login for just dev: developer / $devPass   (also in .env)" }
}
if (-not (Test-Path server.json)) { Copy-Item server.example.json server.json; Write-Host 'wrote server.json' }
