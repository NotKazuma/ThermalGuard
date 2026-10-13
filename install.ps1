# Installs the published exe to Program Files (outside the dev folder), with a Start Menu shortcut.
# Run elevated, after publish.ps1.   Uninstall: install.ps1 -Uninstall
# User data (config, logs) lives in %AppData%\ThermalGuard and is left alone on uninstall.
param([switch]$Uninstall)
$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Run this script from an elevated (Administrator) PowerShell." }

$dir = Join-Path $env:ProgramFiles "ThermalGuard"
$exe = Join-Path $dir "ThermalGuard.exe"
$lnk = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\ThermalGuard.lnk"
$journalPath = Join-Path $env:ProgramData "ThermalGuard\journal.json"

# Stop any running copy. The watchdog is a second ThermalGuard.exe (command line has --watchdog) whose
# whole job is to outlive the main app and restore settings if it dies - killing both at once (as an
# earlier version of this script did) defeats that, so the main app is stopped first and the watchdog
# is given time to finish its own exit before anything touches it.
function Stop-Running {
    $procs = Get-CimInstance Win32_Process -Filter "Name='ThermalGuard.exe'" -ErrorAction SilentlyContinue
    $main = $procs | Where-Object { $_.CommandLine -notmatch '--watchdog' }
    $watchdogs = $procs | Where-Object { $_.CommandLine -match '--watchdog' }

    foreach ($m in $main) {
        $p = Get-Process -Id $m.ProcessId -ErrorAction SilentlyContinue
        if ($p -and $p.MainWindowHandle -ne 0) { [void]$p.CloseMainWindow() }
    }
    Start-Sleep -Seconds 2
    foreach ($m in $main) { Stop-Process -Id $m.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-Process -Name "PresentMon*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    # Give the watchdog(s) a window to detect the main app's exit and replay the journal before
    # anything force-kills them; only step in if one is still alive after a generous wait.
    if ($watchdogs) {
        for ($i = 0; $i -lt 10; $i++) {
            if (-not (Get-Process -Id @($watchdogs.ProcessId) -ErrorAction SilentlyContinue)) { break }
            Start-Sleep -Seconds 1
        }
        foreach ($w in $watchdogs) { Stop-Process -Id $w.ProcessId -Force -ErrorAction SilentlyContinue }
    }

    # Belt and suspenders: if a journal is still on disk (e.g. no watchdog was running to begin with),
    # restore the power cap directly rather than leave it stuck, and never hand off a GUID-shaped cap
    # value - PROCTHROTTLEMAX is a 1-100 percent index, so anything else means the journal was unusable.
    if (Test-Path $journalPath) {
        try {
            $j = Get-Content $journalPath -Raw | ConvertFrom-Json
            if ($j.CapChanged -and $j.OrigAc -ge 1 -and $j.OrigAc -le 100 -and $j.OrigDc -ge 1 -and $j.OrigDc -le 100) {
                powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX $j.OrigAc | Out-Null
                powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX $j.OrigDc | Out-Null
                powercfg /setactive SCHEME_CURRENT | Out-Null
                "Restored CPU cap left over from a previous run (AC=$($j.OrigAc)% DC=$($j.OrigDc)%)."
            }
        } catch { Write-Warning "Could not parse leftover journal at $journalPath - leaving the power cap as-is; check it with 'powercfg /q SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX'." }
        Remove-Item $journalPath -Force -ErrorAction SilentlyContinue
    }
}

if ($Uninstall) {
    Stop-Running
    schtasks /Delete /TN ThermalGuard /F 2>$null | Out-Null   # autostart task, if enabled
    Remove-Item $lnk -Force -ErrorAction SilentlyContinue
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    "Uninstalled. (Settings and logs in %AppData%\ThermalGuard were kept.)"
    return
}

$src = Join-Path $PSScriptRoot "dist\ThermalGuard.exe"
if (-not (Test-Path $src)) { throw "dist\ThermalGuard.exe not found - run publish.ps1 first." }

Stop-Running
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Copy-Item $src $exe -Force

$pm = Get-ChildItem (Join-Path $PSScriptRoot "dist") -Filter "PresentMon*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pm) { Copy-Item $pm.FullName $dir -Force; "Installed: $(Join-Path $dir $pm.Name)" }

$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $dir
$s.Description = "ThermalGuard - game thermal throttling"
$s.Save()

"Installed: $exe"
"Shortcut:  $lnk"
