# uninstall.ps1: remove shell menu, unpin, optionally delete files.
[CmdletBinding()]
param(
    [switch]$KeepQuickAccess,   # leave the Quick Access pins in place
    [switch]$KeepFiles          # leave %LOCALAPPDATA%\CloudRouter on disk
)

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'CloudRouter'

# registry
function Remove-Tree([string]$sub) {
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($sub, $false); Write-Host "  removed HKCU\$sub" } catch {}
}
Write-Host 'Removing shell menu entries...' -ForegroundColor Cyan
Remove-Tree 'Software\Classes\*\shell\CloudRouter'
Remove-Tree 'Software\Classes\Directory\shell\CloudRouter'
Remove-Tree 'Software\Classes\CloudRouter.Sub'

# Quick Access
if (-not $KeepQuickAccess) {
    Write-Host 'Unpinning destinations from Quick Access...' -ForegroundColor Cyan
    $targets = @()
    $cfgPath = Join-Path $installDir 'destinations.json'
    if (Test-Path -LiteralPath $cfgPath) {
        $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
        foreach ($d in $cfg.destinations) {
            $p = [Environment]::ExpandEnvironmentVariables($d.path)
            if ($d.subfolder) { $p = Join-Path $p ([Environment]::ExpandEnvironmentVariables($d.subfolder)) }
            $targets += $p.TrimEnd('\')
        }
    }
    try {
        $qa = (New-Object -ComObject Shell.Application).Namespace('shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}')
        foreach ($item in @($qa.Items())) {
            if ($targets -contains $item.Path.TrimEnd('\')) {
                try { $item.InvokeVerb('unpinfromhome') }
                catch {
                    foreach ($v in $item.Verbs()) {
                        if (($v.Name -replace '&','') -match 'Unpin from Quick access|Remove from Quick access') { $v.DoIt(); break }
                    }
                }
                Write-Host "  unpinned $($item.Path)"
            }
        }
    } catch { Write-Warning "  Quick Access cleanup issue: $_" }
}

# files
if (-not $KeepFiles -and (Test-Path -LiteralPath $installDir)) {
    Write-Host "Removing $installDir ..." -ForegroundColor Cyan
    try { Remove-Item -LiteralPath $installDir -Recurse -Force }
    catch { Write-Warning "  Could not delete $installDir (a CloudRouter.exe may still be running): $_" }
}

# refresh shell
$sig = '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);'
$sh  = Add-Type -MemberDefinition $sig -Name 'CrShellU' -Namespace 'Cr' -PassThru
$sh::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host ''
Write-Host 'Cloud Router uninstalled.' -ForegroundColor Green
