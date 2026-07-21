# publish-release.ps1: self-contained single-file installer to .\dist
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$proj   = $PSScriptRoot
$csproj = Join-Path $proj 'CloudRouter.csproj'
$out    = Join-Path $proj 'publish-release'
$dist   = Join-Path $proj 'dist'

# 1) Embed an .ico as the app icon.
$ico = Join-Path $proj 'CloudRouter.ico'
if (-not (Test-Path -LiteralPath $ico)) {
    Write-Host 'Generating app icon (one-time framework-dependent build)...' -ForegroundColor Cyan
    & (Join-Path $proj 'build.ps1') | Out-Null
    Copy-Item (Join-Path $proj 'publish\CloudRouter.ico') $ico -Force
}

# 2) Self-contained single-file; a fresh PC just double-clicks.
Write-Host 'Publishing self-contained single-file (win-x64)...' -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:ApplicationIcon=CloudRouter.ico -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# 3) Stage the file under its download name.
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$asset = Join-Path $dist 'CloudRouterSetup.exe'
Copy-Item (Join-Path $out 'CloudRouter.exe') $asset -Force

$mb = [math]::Round((Get-Item $asset).Length / 1MB, 1)
Write-Host "Release asset ready: $asset ($mb MB)" -ForegroundColor Green
