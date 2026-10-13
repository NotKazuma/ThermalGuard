using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using ThermalGuard.Hardware;

namespace ThermalGuard.Sensors;

public interface ITempSource : IDisposable
{
    /// <summary>Refreshes readings. Call once per poll before reading temps.</summary>
    void Update();
    int? CpuTemp { get; }
    int? GpuTemp { get; }

    // Extra counters for the dashboard. All optional: a missing value just isn't shown, and none of
    // them ever drive throttling decisions (only CpuTemp/GpuTemp feed ThrottleEngine).
    int? CpuLoad { get; }      // percent
    int? CpuClockMHz { get; }
    int? GpuLoad { get; }      // percent
    int? GpuClockMHz { get; }
    int? GpuPowerW { get; }
}

public static class TempRange
{
    /// <summary>Rejects sensor garbage (0, 255, NaN) so a bad read never drives throttling.</summary>
    public static int? Valid(float? v) =>
        v is float f && !float.IsNaN(f) && f >= 20 && f <= 120 ? (int)Math.Round(f) : null;
}

/// <summary>LibreHardwareMonitor: CPU package temp/load/clock and GPU core temp/load/clock/power.</summary>
public sealed class LhmSource : ITempSource
{
    private readonly Computer _pc = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    public int? CpuTemp { get; private set; }
    public int? GpuTemp { get; private set; }
    public int? CpuLoad { get; private set; }
    public int? CpuClockMHz { get; private set; }
    public int? GpuLoad { get; private set; }
    public int? GpuClockMHz { get; private set; }
    public int? GpuPowerW { get; private set; }

    public LhmSource() => _pc.Open();

    /// <summary>Reads hardware identity from this already-open Computer - never opens a second one.</summary>
    public HardwareInfo DetectHardwareInfo(bool nvidiaSmiAvailable) => HardwareProbe.FromHardware(_pc.Hardware, nvidiaSmiAvailable);

    public void Update()
    {
        float? cpuPkg = null, cpuCoreMax = null, cpuTotalLoad = null, cpuClockMax = null;
        float? gpu = null, gpuLoad = null, gpuClock = null, gpuPower = null;
        foreach (var hw in _pc.Hardware)
        {
            hw.Update();
            foreach (var s in hw.Sensors)
            {
                if (s.Value is not float v) continue;
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    switch (s.SensorType)
                    {
                        case SensorType.Temperature when s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                            cpuPkg = v; break;
                        case SensorType.Temperature:
                            cpuCoreMax = Math.Max(cpuCoreMax ?? v, v); break;
                        case SensorType.Load when s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                            cpuTotalLoad = v; break;
                        case SensorType.Clock when !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase):
                            cpuClockMax = Math.Max(cpuClockMax ?? v, v); break;
                    }
                }
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    switch (s.SensorType)
                    {
                        case SensorType.Temperature:
                            if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || gpu is null) gpu = Math.Max(gpu ?? v, v);
                            break;
                        case SensorType.Load when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || gpuLoad is null:
                            gpuLoad = v; break;
                        case SensorType.Clock when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                            gpuClock = v; break;
                        case SensorType.Power when s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || gpuPower is null:
                            gpuPower = v; break;
                    }
                }
            }
        }
        CpuTemp = TempRange.Valid(cpuPkg ?? cpuCoreMax);
        GpuTemp = TempRange.Valid(gpu);
        CpuLoad = Clamp0To100(cpuTotalLoad);
        CpuClockMHz = (int?)cpuClockMax;
        GpuLoad = Clamp0To100(gpuLoad);
        GpuClockMHz = (int?)gpuClock;
        GpuPowerW = (int?)gpuPower;
    }

    private static int? Clamp0To100(float? v) => v is float f ? Math.Clamp((int)Math.Round(f), 0, 100) : null;

    public void Dispose() => _pc.Close();
}

/// <summary>Fallback GPU-only source (NVIDIA), same approach as the original script, extended with load/clock/power.</summary>
public sealed class NvidiaSmiSource : ITempSource
{
    private readonly string? _smi;
    public int? CpuTemp => null;
    public int? CpuLoad => null;
    public int? CpuClockMHz => null;
    public int? GpuTemp { get; private set; }
    public int? GpuLoad { get; private set; }
    public int? GpuClockMHz { get; private set; }
    public int? GpuPowerW { get; private set; }
    public bool Available => _smi is not null;

    public NvidiaSmiSource(string? smiPath = null) => _smi = smiPath ?? ThermalGuard.Hardware.HardwareProbe.FindNvidiaSmi();

    public void Update()
    {
        GpuTemp = GpuLoad = GpuClockMHz = GpuPowerW = null;
        if (_smi is null) return;
        try
        {
            var psi = new ProcessStartInfo(_smi,
                "--query-gpu=temperature.gpu,utilization.gpu,clocks.sm,power.draw --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var line = p.StandardOutput.ReadLine();
            if (!p.WaitForExit(3000)) { p.Kill(); return; }
            var parts = line?.Split(',', StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
            if (parts.Length >= 4)
            {
                if (int.TryParse(parts[0], out var t)) GpuTemp = TempRange.Valid(t);
                if (int.TryParse(parts[1], out var l)) GpuLoad = Math.Clamp(l, 0, 100);
                if (int.TryParse(parts[2], out var c)) GpuClockMHz = c;
                if (float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) GpuPowerW = (int)Math.Round(w);
            }
        }
        catch (Exception) { /* leave null: callers treat missing as "no reading" */ }
    }

    public void Dispose() { }
}

/// <summary>Uses LHM when it opens; fills a missing GPU reading from nvidia-smi.</summary>
public sealed class CompositeSource : ITempSource
{
    private readonly LhmSource? _lhm;
    private readonly NvidiaSmiSource _smi = new();
    public string LhmError { get; }
    public int? CpuTemp { get; private set; }
    public int? GpuTemp { get; private set; }
    public int? CpuLoad { get; private set; }
    public int? CpuClockMHz { get; private set; }
    public int? GpuLoad { get; private set; }
    public int? GpuClockMHz { get; private set; }
    public int? GpuPowerW { get; private set; }

    public CompositeSource()
    {
        try { _lhm = new LhmSource(); LhmError = ""; }
        catch (Exception e) { LhmError = e.Message; }
    }

    /// <summary>Hardware identity from the Computer instance this source already has open - never
    /// opens a second one (see the note on <see cref="HardwareProbe"/> for why that matters).</summary>
    public HardwareInfo DetectHardwareInfo() =>
        _lhm?.DetectHardwareInfo(_smi.Available) ?? HardwareProbe.Unknown();

    public void Update()
    {
        _lhm?.Update();
        CpuTemp = _lhm?.CpuTemp; CpuLoad = _lhm?.CpuLoad; CpuClockMHz = _lhm?.CpuClockMHz;
        GpuTemp = _lhm?.GpuTemp; GpuLoad = _lhm?.GpuLoad; GpuClockMHz = _lhm?.GpuClockMHz; GpuPowerW = _lhm?.GpuPowerW;

        if (GpuTemp is null && _smi.Available)
        {
            _smi.Update();
            GpuTemp = _smi.GpuTemp; GpuLoad ??= _smi.GpuLoad; GpuClockMHz ??= _smi.GpuClockMHz; GpuPowerW ??= _smi.GpuPowerW;
        }
    }

    public void Dispose() { _lhm?.Dispose(); _smi.Dispose(); }
}
