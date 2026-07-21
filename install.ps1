# install.ps1: build, install, register the shell submenu.
# Per-user (HKCU), no admin; re-run to refresh menu.
[CmdletBinding()]
param(
    [switch]$SkipBuild,        # use the existing .\publish output as-is
    [switch]$PinQuickAccess    # also pin each destination to Quick Access
)

$ErrorActionPreference = 'Stop'
$proj       = $PSScriptRoot
$pub        = Join-Path $proj 'publish'
$installDir = Join-Path $env:LOCALAPPDATA 'CloudRouter'

# build
if (-not $SkipBuild) {
    & (Join-Path $proj 'build.ps1')
}
if (-not (Test-Path -LiteralPath (Join-Path $pub 'CloudRouter.exe'))) {
    throw "publish\CloudRouter.exe not found. Run build.ps1 first (or omit -SkipBuild)."
}

# install
Write-Host "Installing to $installDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $pub '*') -Destination $installDir -Recurse -Force

$exe = Join-Path $installDir 'CloudRouter.exe'
$ico = Join-Path $installDir 'CloudRouter.ico'

$cfg   = Get-Content -LiteralPath (Join-Path $installDir 'destinations.json') -Raw | ConvertFrom-Json
$dests = $cfg.destinations

# registry
function New-Key([string]$sub) {
    return [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($sub, $true)
}
function Remove-Tree([string]$sub) {
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($sub, $false) } catch {}
}

# Clear previous registration so removals leave no orphans.
Remove-Tree 'Software\Classes\*\shell\CloudRouter'
Remove-Tree 'Software\Classes\Directory\shell\CloudRouter'
Remove-Tree 'Software\Classes\CloudRouter.Sub'

# Two parent verbs share one submenu key.
foreach ($parent in @('Software\Classes\*\shell\CloudRouter',
                       'Software\Classes\Directory\shell\CloudRouter')) {
    $k = New-Key $parent
    $k.SetValue('MUIVerb', 'Cloud Router')
    $k.SetValue('Icon', $ico)
    $k.SetValue('ExtendedSubCommandsKey', 'CloudRouter.Sub')
    $k.Close()
}

# The submenu entries, numbered to control order.
$i = 0
foreach ($d in $dests) {
    $i++
    $keyName = '{0:00}{1}' -f $i, $d.key
    $base = "Software\Classes\CloudRouter.Sub\shell\$keyName"

    $k = New-Key $base
    $k.SetValue('MUIVerb', $d.name)
    if ($d.icon) { $k.SetValue('Icon', $d.icon) }
    $k.Close()

    $c = New-Key "$base\command"
    $c.SetValue('', ('"{0}" {1} {2} "%1"' -f $exe, $cfg.action, $d.key))
    $c.Close()
}
Write-Host "  Registered $i destinations under the Cloud Router submenu." -ForegroundColor Green

# Quick Access
function Pin-QuickAccess([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { Write-Warning "  Skip pin (folder missing): $path"; return }
    try {
        $shell = New-Object -ComObject Shell.Application
        $self  = $shell.Namespace($path).Self
        # pintohome no-ops on virtual folders; match by name.
        $done = $false
        foreach ($v in $self.Verbs()) {
            if (($v.Name -replace '&','') -match 'Pin to Quick access') { $v.DoIt(); $done = $true; break }
        }
        if (-not $done) { $self.InvokeVerb('pintohome') }
    } catch { Write-Warning "  Pin failed for $path : $_" }
}

if ($PinQuickAccess) {
    Write-Host 'Pinning destinations to Quick Access (for the Save As dialog)...' -ForegroundColor Cyan
    foreach ($d in $dests) {
        $p = [Environment]::ExpandEnvironmentVariables($d.path)
        if ($d.subfolder) { $p = Join-Path $p ([Environment]::ExpandEnvironmentVariables($d.subfolder)) }
        Pin-QuickAccess $p
    }
}

# refresh shell
$sig = '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);'
$sh  = Add-Type -MemberDefinition $sig -Name 'CrShell' -Namespace 'Cr' -PassThru
$sh::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

Write-Host ''
Write-Host 'Cloud Router installed.' -ForegroundColor Green
Write-Host 'Right-click any file or folder -> "Show more options" -> Cloud Router.' -ForegroundColor Green
if ($PinQuickAccess) {
    Write-Host 'Destinations are also pinned under Quick access in the Save As dialog.' -ForegroundColor Green
}
