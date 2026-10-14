# ThermalGuard

Windows tray app that watches CPU/GPU temperature while a game runs and throttles to stop a laptop
overheating. Successor to the original `Thermal-Guard.ps1`.

| Level | Trigger (defaults) | Action |
|---|---|---|
| Warm | GPU 78 / CPU 85 C | CPU max 80%, GPU power limit 100% (no-op) |
| Hot | GPU 83 / CPU 92 C | CPU max 50%, GPU power limit 85%, game priority Below Normal |
| Critical | GPU 87 / CPU 96 C | CPU max 30%, GPU power limit 70%, optional 1.5 s suspend (off by default) |

Everything is restored when temperatures drop, when the game exits, on Exit, and after a crash or hard kill.

## Requirements
- Windows 10/11 x64, run as Administrator (the exe asks for it).
- **PawnIO driver** for CPU temperatures: `winget install namazso.PawnIO`. Without it you get GPU only (via nvidia-smi on NVIDIA); the tray shows `CPU n/a` and tells you when no sensor works.
- **GPU power-limit lever**: NVIDIA via the bundled `nvidia-smi`-style calls, if your driver/GPU actually allows setting it (some laptop GPUs report a range but reject every set - ThermalGuard verifies this for real at startup rather than assuming, and simply skips the lever if it doesn't work). AMD support exists via ADL but is **untested** - no AMD GPU was available during development; it fails safe (skips the lever) if anything doesn't match.
- **FPS**: bundled (`tools/PresentMon`, Intel's PresentMon v2.6.0, MIT licensed - see its provenance file for the verified source/hash). No extra install needed.

## Install
```powershell
.\publish.ps1          # builds dist\ThermalGuard.exe (self-contained, ~70 MB) + bundles PresentMon
.\install.ps1          # elevated: copies to C:\Program Files\ThermalGuard + Start Menu shortcut
.\install.ps1 -Uninstall
```
Settings and logs stay in `%AppData%\ThermalGuard` after uninstall.

## Use
Launch from the Start Menu; it lives in the tray. Menu: Pause/Resume, **Dashboard** (live temps, load, clocks, power, FPS, cap levels), Pick game, Settings, Open log folder, Exit.
Settings has profiles (thresholds, CPU + GPU caps, priority, suspend), auto-detect, and Start with Windows (an elevated Task Scheduler task, so no UAC prompt at logon). Do this from the installed exe so the task points at Program Files.

- **Profile match:** an enabled profile's exe name wins.
- **Auto-detect:** a fullscreen foreground app that holds for 10 s gets the Default profile. Browsers, launchers, media players and terminals are never picked.
- **Protected game** (anti-cheat) flag: power caps (CPU + GPU) only. Priority and suspend are skipped, because they act on the game process.
- **Hardware auto-detect:** CPU and GPU vendor/model are identified at startup (`--check` prints them); sensor source and GPU lever are picked automatically, nothing to configure.
- Logs: `%AppData%\ThermalGuard\logs\thermal-YYYY-MM-DD.csv` (14 days kept).

## Safety design
1. Every change (CPU cap, GPU power limit, priority, suspend) is written to `%ProgramData%\ThermalGuard\journal.json` before it is made.
2. A watchdog (the same exe with `--watchdog`) waits for the app to die and replays the journal, including resuming a suspended game and restoring the GPU power limit.
3. On startup, any leftover journal is recovered.
4. No valid temperature for 5 polls in a row: throttling is released, never applied blind.
5. `install.ps1` never kills the main app and its watchdog in the same instant - that would defeat point 2 above - and independently restores the power cap from a leftover journal as a last resort.

## Develop
```powershell
dotnet build
dotnet test                         # unit tests, no hardware needed
tests\kill-test.ps1 [-Exe path]     # elevated: throttles a dummy game, hard-kills the app, checks restore
ThermalGuard.exe --check            # read-only: power scheme, hardware, GPU-cap support, sensor readings
ThermalGuard.exe --run <process>    # headless mode for one process
```
Layout: `src/ThermalGuard` (`Core` engine, `Safety` journal/watchdog/actuators/GPU power, `Sensors`, `Hardware` auto-detect, `Detection`, `Config`, `Perf` FPS, `Logging`, `Startup`, `UI`), `tests/ThermalGuard.Tests`, `tools/PresentMon` (vendored, see its own README).

## Known limits
- Suspend freezes the tray UI for its duration; it is opt-in and rate-limited.
- Per-profile `PollSeconds` is used by `--run`; the tray loop ticks every 2 s.
- Verified live against a real game session (temps, CPU+GPU caps, priority, suspend, crash/kill recovery, extended counters). Still not verified: the fullscreen auto-detect heuristic against a real game (only explicit profile matching has been), sleep/resume, logoff/shutdown, and any game with kernel-level anti-cheat.
- AMD GPU power-limit control is built but unverified - see Requirements above.
- Unsigned exe: SmartScreen will warn. LibreHardwareMonitor's driver can be flagged by antivirus, and some anti-cheats object to hardware-monitor drivers.
- `PROCTHROTTLEMAX` effectiveness varies by CPU; check the logs on the target laptop.

## Related projects
- [**FrameLift**](https://github.com/NotKazuma/FrameLift): my newer all-in-one game optimizer (Game Boost, heat protection, PC health check, full undo). It builds on ThermalGuard's heat protection and StutterFix's game tuning. Run only one of them at a time; FrameLift's heat protection stands down while ThermalGuard is running.
- [**StutterFix**](https://github.com/NotKazuma/StutterFix): reduces game stutter (priority, background apps, standby memory, frame-time meter).