<#
  sign.ps1 - Optional Authenticode signing for the built binaries.

  Does NOTHING unless you provide a signing config, so it is safe to call from
  every build. To enable: copy signing.example.json to signing.json (git-ignored),
  fill it in, and rebuild. Supports three methods:

    * trustedsigning - Azure Trusted Signing (~$10/mo, cloud, no token).
                       Auth comes from your Azure login or the
                       AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET
                       environment variables. Requires the NuGet package
                       "Microsoft.Trusted.Signing.Client" (its bin\x64 folder
                       holds Azure.CodeSigning.Dlib.dll - point "dlib" at it).
    * pfx            - a .pfx certificate file + password.
    * thumbprint     - a cert already in your store / on a hardware token (EV).
#>
[CmdletBinding()]
param(
    [string[]]$Files,
    [string]$ConfigPath
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

if (-not $ConfigPath) { $ConfigPath = Join-Path $here 'signing.json' }
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    Write-Host "No signing config; skipping signing. (Copy signing.example.json to signing.json to enable.)" -ForegroundColor DarkGray
    return
}
if (-not $Files -or $Files.Count -eq 0) { $Files = @(Join-Path $here 'publish\CloudRouter.exe') }

$cfg = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$ts  = if ($cfg.timestampUrl) { $cfg.timestampUrl } else { 'http://timestamp.acs.microsoft.com' }

# Find the newest 64-bit signtool.exe from the installed Windows SDK.
$signtool = @('C:\Program Files (x86)\Windows Kits\10\bin', 'C:\Program Files\Windows Kits\10\bin') |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-ChildItem $_ -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue } |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) {
    throw "signtool.exe not found. Install the Windows SDK (e.g. 'winget install Microsoft.WindowsSDK')."
}

foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file)) { throw "File to sign not found: $file" }

    $stArgs = @('sign', '/v', '/fd', 'SHA256', '/tr', $ts, '/td', 'SHA256')
    switch ($cfg.method) {
        'trustedsigning' {
            $dlib = [Environment]::ExpandEnvironmentVariables([string]$cfg.dlib)
            if (-not (Test-Path -LiteralPath $dlib)) {
                throw "Trusted Signing dlib not found: $dlib`nAdd the 'Microsoft.Trusted.Signing.Client' NuGet package and point 'dlib' at its bin\x64\Azure.CodeSigning.Dlib.dll."
            }
            $meta = [ordered]@{
                Endpoint               = $cfg.endpoint
                CodeSigningAccountName = $cfg.account
                CertificateProfileName = $cfg.profile
            } | ConvertTo-Json
            $metaPath = Join-Path $env:TEMP 'cloudrouter_ts_meta.json'
            $meta | Set-Content -LiteralPath $metaPath -Encoding UTF8
            $stArgs += @('/dlib', $dlib, '/dmdf', $metaPath)
        }
        'pfx' {
            $stArgs += @('/f', [Environment]::ExpandEnvironmentVariables([string]$cfg.pfx))
            if ($cfg.password) { $stArgs += @('/p', [string]$cfg.password) }
        }
        'thumbprint' {
            $stArgs += @('/sha1', [string]$cfg.thumbprint)
        }
        default {
            throw "Unknown signing method '$($cfg.method)'. Use trustedsigning, pfx, or thumbprint."
        }
    }
    $stArgs += $file

    Write-Host "Signing $(Split-Path $file -Leaf) ..." -ForegroundColor Cyan
    & $signtool @stArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
}

Write-Host 'Signing complete.' -ForegroundColor Green
