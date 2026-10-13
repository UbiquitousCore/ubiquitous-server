$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..' '..')

dotnet build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test --no-build
exit $LASTEXITCODE
