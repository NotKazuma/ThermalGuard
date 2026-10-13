# Third-party notices

ThermalGuard is MIT licensed (see [LICENSE](LICENSE)). It depends on, and in one case bundles, the
following third-party software, unmodified:

## LibreHardwareMonitorLib
- License: MPL-2.0
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Used as: a NuGet package reference (`LibreHardwareMonitorLib`, v0.9.6) for CPU/GPU sensor readings.
  Not modified; its source is not redistributed as part of this repository.

## PresentMon
- License: MIT
- Source: https://github.com/GameTechDev/PresentMon
- Used as: a vendored, unmodified binary (`tools/PresentMon/PresentMon-2.6.0-x64.exe`), published
  alongside ThermalGuard.exe for live FPS readings. See [tools/PresentMon/README-provenance.txt](tools/PresentMon/README-provenance.txt)
  for the exact release, download date and SHA256 verification.

## PawnIO
- License: see https://github.com/namazso/PawnIO
- Not bundled. LibreHardwareMonitorLib uses it at runtime (as an external driver, installed separately
  by the user, e.g. via `winget install namazso.PawnIO`) to read CPU temperature sensors.
