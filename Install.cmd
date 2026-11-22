@echo off
rem VISFX Addons for VEGAS Pro - installs SmartZoom_v4.cs and "Smart Batch Stabilization.cs"
rem automatically into the Vegas Pro Script Menu folder (MAGIX, Boris FX and other layouts).
rem Close VEGAS Pro first if it is open. Admin rights are requested when needed.
rem Uninstall: Install.cmd /uninstall

set "ADDONS_HERE=%~dp0"
set "ADDONS_SELF=%~f0"
set "ADDONS_UNINSTALL="
if /i "%~1"=="/uninstall" set "ADDONS_UNINSTALL=1"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s = [IO.File]::ReadAllText($env:ADDONS_SELF); Invoke-Expression $s.Substring($s.IndexOf('#' + 'PS-START'))"
exit /b

#PS-START
# Copies the two scripts into every VEGAS Pro Script Menu folder found.
$ErrorActionPreference = 'Stop'
$here = $env:ADDONS_HERE
$uninstall = [bool]$env:ADDONS_UNINSTALL
$scriptFiles = @('SmartZoom_v4.cs', 'Smart Batch Stabilization.cs')

function Done($msg, $ok) {
    Write-Host ''
    if ($ok) { Write-Host $msg -ForegroundColor Green } else { Write-Host $msg -ForegroundColor Red }
    Write-Host ''
    Read-Host 'Press Enter to close'
    exit
}

# Close running VEGAS Pro instances first
while (Get-Process -Name 'vegas*' -ErrorAction SilentlyContinue) {
    Write-Host 'VEGAS Pro is open. Close it, then press Enter (or Ctrl+C to cancel).' -ForegroundColor Yellow
    Read-Host | Out-Null
}

if (-not $uninstall) {
    foreach ($f in $scriptFiles) {
        if (-not (Test-Path (Join-Path $here $f))) {
            Done ("Script not found next to this file: " + $f + "`nExtract the whole zip (right-click > Extract All) and run Install.cmd again.") $false
        }
    }
}

# Find Script Menu folders:
#  - MAGIX:     C:\Program Files\VEGAS\VEGAS Pro XX.0\Script Menu
#  - Boris FX:  C:\Program Files\BorisFX\Vegas Pro 2026\Script Menu
#  - Sony etc.: C:\Program Files\Sony\Vegas Pro 13.0\Script Menu
$scriptMenuFolders = @()
foreach ($root in @($env:ProgramFiles, 'C:\Program Files (x86)')) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $scriptMenuFolders += Get-ChildItem -LiteralPath $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'Script Menu' -and $_.FullName -ilike '*vegas*' } |
        Select-Object -ExpandProperty FullName
}
# User-level locations (added only if they exist)
foreach ($r in @((Join-Path $env:APPDATA 'Vegas Pro'), (Join-Path $env:APPDATA 'VEGAS Pro'),
                 (Join-Path $env:LOCALAPPDATA 'Vegas Pro'), (Join-Path $env:LOCALAPPDATA 'VEGAS Pro'))) {
    $p = Join-Path $r 'Script Menu'
    if (Test-Path -LiteralPath $p) { $scriptMenuFolders += $p }
}
$scriptMenuFolders = $scriptMenuFolders | Select-Object -Unique

if ($scriptMenuFolders.Count -eq 0) {
    Done 'No VEGAS Pro Script Menu folder was found. Is Vegas Pro installed?' $false
}

# Elevate to admin when needed (writes to Program Files require it)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$canWrite = $true
try {
    if (-not (Test-Path -LiteralPath $scriptMenuFolders[0])) { New-Item -LiteralPath $scriptMenuFolders[0] -ItemType Directory -Force | Out-Null }
    $probe = Join-Path $scriptMenuFolders[0] ('.addons-' + [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($probe, ''); Remove-Item -LiteralPath $probe -Force
} catch { $canWrite = $false }
if (-not $canWrite -and -not $isAdmin) {
    Write-Host 'Administrator rights are needed: Windows will ask for permission.'
    $argList = '/c ""' + $env:ADDONS_SELF + '"' + $(if ($uninstall) { ' /uninstall' } else { '' }) + '"'
    try { Start-Process cmd.exe -Verb RunAs -ArgumentList $argList } catch { Done 'Permission was refused: nothing was changed.' $false }
    exit 0
}

$installed = @(); $failed = 0
foreach ($f in $scriptMenuFolders) {
    try {
        if ($uninstall) {
            foreach ($sc in $scriptFiles) { Remove-Item -LiteralPath (Join-Path $f $sc) -Force -ErrorAction SilentlyContinue }
            $installed += $f
        } else {
            foreach ($sc in $scriptFiles) { Copy-Item -LiteralPath (Join-Path $here $sc) -Destination $f -Force }
            # Sounds folder: Smart Batch Stabilization plays these from the Script Menu folder
            $soundsSrc = Join-Path $here 'Sounds'
            if (Test-Path -LiteralPath $soundsSrc) {
                $soundsDst = Join-Path $f 'Sounds'
                New-Item -LiteralPath $soundsDst -ItemType Directory -Force | Out-Null
                Get-ChildItem -LiteralPath $soundsSrc -File -ErrorAction SilentlyContinue | Copy-Item -Destination $soundsDst -Force
            }
            $installed += $f
        }
    } catch { $failed++ }
}

if ($uninstall) {
    Done ('Smart scripts were removed from: ' + ($scriptMenuFolders -join ', ')) $true
}
if ($failed -eq 0 -and $installed.Count -gt 0) {
    Done ("Installed into " + $installed.Count + " folder(s):`n" + ($installed -join "`n") + "`n`nOpen VEGAS Pro and use the scripts from: Script Menu > SmartZoom_v4 / Smart Batch Stabilization.") $true
}
Done 'Install failed: nothing was changed.' $false
