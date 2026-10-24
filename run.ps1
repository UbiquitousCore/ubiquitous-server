# Run the server with nothing but Docker Desktop (Windows). No .NET, just or
# Python needed. Developers: see setup.ps1 instead.
#   .\run.ps1 ingest   build the data package from the .ipa in incoming\ (once)
#   .\run.ps1 start    start the server and Postgres, wait until healthy
#   .\run.ps1 stop     stop them (saves are kept)
#   .\run.ps1 logs     follow the server log
#   .\run.ps1 account create <name> | password <name> | list   (server must be running)
param([string]$Command = 'help', [Parameter(ValueFromRemainingArguments)][string[]]$Rest)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is not installed: https://docs.docker.com/desktop/setup/install/windows-install/' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker is installed but not running - start Docker Desktop and try again' }

& (Join-Path $PSScriptRoot 'scripts\setup\init-config.ps1')
Set-Location $PSScriptRoot

$compose = @('compose')
if (Test-Path server.json) { $compose += @('-f', 'docker-compose.yml', '-f', 'docker-compose.server-json.yml') }

switch ($Command) {
    'ingest' {
        $found = Get-ChildItem incoming -Recurse -Depth 2 -Include *.ipa, tables.bin -ErrorAction SilentlyContinue
        if (-not $found) { throw 'Copy your client .ipa into incoming\ first, then run .\run.ps1 ingest again' }
        docker @compose run --rm --build ingest
        if ($LASTEXITCODE -ne 0) { throw 'ingest failed' }
        Write-Host 'Done. Start the server with: .\run.ps1 start (restart it if it was already running)'
    }
    'start' {
        docker @compose up --build -d --wait
        if ($LASTEXITCODE -ne 0) { throw 'the stack did not become healthy - see: .\run.ps1 logs' }
        Write-Host 'Server healthy: ws://127.0.0.1:8781/'
        Write-Host 'Nobody can sign in until an account exists: .\run.ps1 account create <name>'
    }
    'stop' { docker @compose down }
    'logs' { docker @compose logs -f server }
    'account' { docker @compose exec server dotnet server.dll account @Rest }
    default { Get-Content $PSCommandPath -TotalCount 8 | Select-Object -Skip 1 | ForEach-Object { $_ -replace '^# ?', '' } }
}
