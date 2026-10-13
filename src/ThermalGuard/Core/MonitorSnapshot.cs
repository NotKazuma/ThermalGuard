namespace ThermalGuard.Core;

/// <summary>One tick's worth of state, pushed from TrayForm to whatever wants to display it (Dashboard).</summary>
public sealed record MonitorSnapshot(
    bool Attached,
    string Game,
    Level Level,
    int? CpuTemp, int? CpuLoad, int? CpuClockMHz, int CpuCapPercent,
    int? GpuTemp, int? GpuLoad, int? GpuClockMHz, int? GpuPowerW, int GpuCapPercent, bool GpuCapSupported,
    int? Fps, bool FpsAvailable,
    string CpuName, string GpuName,
    bool ProtectedGame, bool SuspendEnabled);
