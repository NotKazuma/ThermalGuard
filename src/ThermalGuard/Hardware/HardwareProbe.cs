using LibreHardwareMonitor.Hardware;

namespace ThermalGuard.Hardware;

public enum GpuVendor { None, Nvidia, Amd, Intel, Other }

public sealed record HardwareInfo(
    string CpuName,
    string GpuName,
    GpuVendor GpuVendor,
    bool NvidiaSmiAvailable);

/// <summary>
/// Identifies the CPU and primary discrete GPU so the rest of the app can pick the right sensors and
/// the right (if any) power-cap lever automatically, instead of asking the user to configure it.
///
/// Only ever reads from a <see cref="Computer"/> instance the caller already owns and has open -
/// never opens one of its own. LibreHardwareMonitor's underlying driver session is not safe to open a
/// second time in the same process: doing so here (a short-lived Computer just for detection, right
/// before the long-lived one used for ongoing polling) corrupted internal state that only surfaced as
/// a NullReferenceException inside LHM's own Update() several polls later - found via a real crash in
/// `--run` (which polls in a loop) that `--check` (a single read) never hit.
/// </summary>
public static class HardwareProbe
{
    public static HardwareInfo FromHardware(IEnumerable<IHardware> hardware, bool nvidiaSmiAvailable)
    {
        string cpuName = "Unknown CPU";
        string gpuName = "No GPU detected";
        var vendor = GpuVendor.None;

        foreach (var hw in hardware)
        {
            if (hw.HardwareType == HardwareType.Cpu && cpuName == "Unknown CPU")
                cpuName = hw.Name;

            // Prefer a discrete GPU over an integrated one if both are present.
            var v = hw.HardwareType switch
            {
                HardwareType.GpuNvidia => GpuVendor.Nvidia,
                HardwareType.GpuAmd => GpuVendor.Amd,
                HardwareType.GpuIntel => GpuVendor.Intel,
                _ => GpuVendor.None,
            };
            if (v != GpuVendor.None && (vendor == GpuVendor.None || vendor == GpuVendor.Intel))
            {
                vendor = v;
                gpuName = hw.Name;
            }
        }

        return new HardwareInfo(cpuName, gpuName, vendor, nvidiaSmiAvailable);
    }

    public static HardwareInfo Unknown() => new("Unknown CPU", "No GPU detected", GpuVendor.None, FindNvidiaSmi() is not null);

    /// <summary>
    /// Opens its own short-lived Computer to detect hardware. Only safe to call when nothing else in
    /// this process opens a Computer of its own (see the type-level note) - currently only the
    /// watchdog's one-shot crash recovery, which does nothing else and exits right after.
    /// </summary>
    public static HardwareInfo Detect()
    {
        var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        try { pc.Open(); return FromHardware(pc.Hardware, FindNvidiaSmi() is not null); }
        finally { pc.Close(); }
    }

    public static string? FindNvidiaSmi()
    {
        foreach (var p in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
        })
            if (File.Exists(p)) return p;
        return null;
    }
}
