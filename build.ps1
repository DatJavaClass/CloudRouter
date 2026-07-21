# build.ps1: compile Cloud Router, emit icon to .\publish
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$proj    = $PSScriptRoot
$csproj  = Join-Path $proj 'CloudRouter.csproj'
$pub     = Join-Path $proj 'publish'

# Ensure destinations.json exists (cloned repos ship the example).
$cfg = Join-Path $proj 'destinations.json'
if (-not (Test-Path -LiteralPath $cfg)) {
    Copy-Item (Join-Path $proj 'destinations.example.json') $cfg
    Write-Host 'No destinations.json found; created one from destinations.example.json. Edit it, then re-run install.ps1.' -ForegroundColor Yellow
}

Write-Host 'Building Cloud Router (Release)...' -ForegroundColor Cyan
dotnet publish $csproj -c Release -o $pub
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $pub 'CloudRouter.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Build succeeded but $exe is missing." }

Write-Host 'Generating CloudRouter.ico...' -ForegroundColor Cyan
$ico = Join-Path $pub 'CloudRouter.ico'
Start-Process -FilePath $exe -ArgumentList '--makeicon', "`"$ico`"" -Wait -NoNewWindow
if (-not (Test-Path -LiteralPath $ico)) { throw 'Icon generation failed.' }

# Optional Authenticode signing; no-op without signing.json.
& (Join-Path $proj 'sign.ps1') -Files @($exe, (Join-Path $pub 'CloudRouter.dll'))

Write-Host "Done. Output: $pub" -ForegroundColor Green
