# Real-system kill test: throttle a dummy game, hard-kill the app, expect the watchdog to restore everything.
# Run elevated. Temporarily changes the active scheme's CPU max, then verifies it is put back.
param([string]$Exe = (Join-Path $PSScriptRoot "..\src\ThermalGuard\bin\Debug\net8.0-windows\ThermalGuard.exe"))
$ErrorActionPreference = "Stop"
$exe = $Exe
$journal = Join-Path $env:ProgramData "ThermalGuard\journal.json"

function CapsOnly { ((powercfg /q SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX | Select-String "Power Setting Index" | ForEach-Object { [Convert]::ToInt32(($_.ToString().Split(":")[1]).Trim(),16) }) -join "/") }

$dummy = Join-Path $env:TEMP "tgdummy.exe"
Copy-Item "$env:SystemRoot\System32\PING.EXE" $dummy -Force
$game = Start-Process $dummy -ArgumentList "-t 127.0.0.1" -WindowStyle Hidden -PassThru
$before = CapsOnly; $prioBefore = $game.PriorityClass
"before:  caps(AC/DC)=$before  priority=$prioBefore"

$app = Start-Process $exe -ArgumentList "--run tgdummy --test" -PassThru -WindowStyle Hidden
Start-Sleep 10
$game.Refresh()
"running: caps(AC/DC)=$(CapsOnly)  priority=$($game.PriorityClass)  journal=$(Test-Path $journal)"

Stop-Process -Id $app.Id -Force      # hard kill: no finally blocks, no handlers
Start-Sleep 5
$game.Refresh()
$after = CapsOnly
"after:   caps(AC/DC)=$after  priority=$($game.PriorityClass)  journal=$(Test-Path $journal)"

Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
Remove-Item $dummy -Force -ErrorAction SilentlyContinue

$ok = ($after -eq $before) -and ($game.PriorityClass -eq $prioBefore) -and -not (Test-Path $journal)
if ($ok) { "PASS: watchdog restored everything after a hard kill" } else { "FAIL"; exit 1 }
