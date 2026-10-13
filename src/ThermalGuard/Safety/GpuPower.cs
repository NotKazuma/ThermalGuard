using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ThermalGuard.Hardware;

namespace ThermalGuard.Safety;

/// <summary>
/// Vendor-agnostic GPU power-limit lever. Values are percent of the GPU's own default/rated limit
/// (matching the CPU cap's percent-of-max style), translated to whatever units the vendor API wants.
/// </summary>
public interface IGpuPower
{
    /// <summary>True only after a real runtime probe succeeds - never assumed from vendor name alone.</summary>
    bool Supported { get; }
    int? GetLimitW();
    void SetLimitPercent(int percent);
    void RestoreDefault();
}

public sealed class NullGpuPower : IGpuPower
{
    public bool Supported => false;
    public int? GetLimitW() => null;
    public void SetLimitPercent(int percent) { }
    public void RestoreDefault() { }
}

/// <summary>
/// NVIDIA via nvidia-smi -pl (documented, stable CLI shipped with every NVIDIA driver). Verified
/// against a real GTX 1060 during development.
/// </summary>
public sealed class NvidiaGpuPower : IGpuPower
{
    private readonly string _smi;
    private int _minW, _maxW, _defaultW;
    public bool Supported { get; }

    public NvidiaGpuPower(string smiPath)
    {
        _smi = smiPath;
        try
        {
            var o = Query("--query-gpu=power.min_limit,power.max_limit,power.default_limit --format=csv,noheader,nounits");
            var parts = o.Split(',', StringSplitOptions.TrimEntries);
            _minW = (int)Math.Round(ParseW(parts[0]));
            _maxW = (int)Math.Round(ParseW(parts[1]));
            _defaultW = (int)Math.Round(ParseW(parts[2]));
            bool rangeOk = _defaultW > 0 && _maxW >= _minW;

            // Reading the range is not proof it can actually be set: some laptop/OEM GPUs report a
            // valid min/max/default via nvidia-smi but reject every -pl call with "not supported",
            // while still exiting 0 ("treating as warning"). Verified on a real GTX 1060 laptop GPU.
            // The only reliable check is to attempt a real set (to the current default - a no-op) and
            // read nvidia-smi's own text for the rejection, since the exit code cannot be trusted.
            Supported = rangeOk && SetReturnsOk($"-pl {_defaultW}");
        }
        catch (Exception) { Supported = false; }
    }

    public int? GetLimitW()
    {
        if (!Supported) return null;
        try { return (int)Math.Round(ParseW(Query("--query-gpu=power.limit --format=csv,noheader,nounits"))); }
        catch (Exception) { return null; }
    }

    public void SetLimitPercent(int percent)
    {
        if (!Supported) return;
        int watts = Math.Clamp((int)Math.Round(_defaultW * percent / 100.0), _minW, _maxW);
        Run($"-pl {watts}");
    }

    public void RestoreDefault()
    {
        if (Supported) Run($"-pl {_defaultW}");
    }

    private bool SetReturnsOk(string args)
    {
        var (stdout, stderr) = RunCaptured(args);
        var combined = stdout + stderr;
        return !combined.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            && !combined.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
            && !combined.Contains("unable to set", StringComparison.OrdinalIgnoreCase);
    }

    private static float ParseW(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private string Query(string args)
    {
        var psi = new ProcessStartInfo(_smi, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var line = p.StandardOutput.ReadLine() ?? throw new InvalidOperationException("nvidia-smi gave no output");
        if (!p.WaitForExit(3000)) { p.Kill(); throw new TimeoutException("nvidia-smi timed out"); }
        return line;
    }

    private void Run(string args) => RunCaptured(args);

    private (string stdout, string stderr) RunCaptured(string args)
    {
        var psi = new ProcessStartInfo(_smi, args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(3000)) { try { p.Kill(); } catch (Exception) { } }
        return (stdout, stderr);
    }
}

/// <summary>
/// AMD via ADL (AMD Display Library), Overdrive6's PowerControl level - the same mechanism AMD's own
/// WattMan and third-party tools (OverdriveNTool, HWiNFO) use to adjust the power limit.
///
/// EXPERIMENTAL / UNTESTED: built without any AMD GPU available to verify against. Overdrive6 targets
/// older GCN-generation cards; newer RDNA/RDNA2/RDNA3 GPUs use a different "OverdriveN" API this does
/// not implement, so on those it will most likely report Supported=false and the app will simply skip
/// the GPU cap for AMD (the CPU cap and priority/suspend levers still apply). If you have AMD hardware,
/// please test this and report back - do not assume it works.
/// </summary>
public sealed class AmdGpuPower : IGpuPower
{
    private delegate IntPtr ADL_Main_Memory_Alloc(int size);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Main_Control_Create")] private static extern int Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Main_Control_Destroy")] private static extern int Destroy();
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Adapter_NumberOfAdapters_Get")] private static extern int NumAdapters(ref int num);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Adapter_Active_Get")] private static extern int AdapterActive(int adapterIndex, ref int status);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Overdrive6_PowerControlInfo_Get")] private static extern int PowerControlInfoGet(int adapterIndex, ref ODPowerControlInfo info);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Overdrive6_PowerControl_Get")] private static extern int PowerControlGet(int adapterIndex, ref int level, ref int defaultLevel);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL_Overdrive6_PowerControl_Set")] private static extern int PowerControlSet(int adapterIndex, int level);

    [StructLayout(LayoutKind.Sequential)]
    private struct ODPowerControlInfo { public int MinValue, MaxValue, StepValue; }

    private readonly int _adapterIndex;
    private readonly int _minLevel, _maxLevel, _defaultLevel;
    private readonly List<IntPtr> _allocs = new();
    public bool Supported { get; }

    public AmdGpuPower()
    {
        try
        {
            if (Create(Alloc, 1) != 0) return;
            int n = 0;
            if (NumAdapters(ref n) != 0 || n <= 0) return;

            // Use the first active adapter. A full multi-GPU system would need AdapterInfo enumeration
            // to pick the right index; out of scope without hardware to validate it against.
            int active = 0;
            if (AdapterActive(0, ref active) != 0 || active == 0) return;
            _adapterIndex = 0;

            var info = new ODPowerControlInfo();
            if (PowerControlInfoGet(_adapterIndex, ref info) != 0) return;
            _minLevel = info.MinValue; _maxLevel = info.MaxValue;

            int level = 0, def = 0;
            if (PowerControlGet(_adapterIndex, ref level, ref def) != 0) return;
            _defaultLevel = def;

            Supported = _maxLevel > _minLevel;
        }
        catch (Exception) { Supported = false; }
        finally { foreach (var p in _allocs) Marshal.FreeHGlobal(p); _allocs.Clear(); }
    }

    private IntPtr Alloc(int size) { var p = Marshal.AllocHGlobal(size); _allocs.Add(p); return p; }

    public int? GetLimitW() => null; // ADL reports a relative percent level, not watts

    public void SetLimitPercent(int percent)
    {
        if (!Supported) return;
        // Profile percent (e.g. 100/80/60, "of default") -> ADL's level, which is a +/- offset from
        // the default (e.g. -20..0..+20), clamped to what this adapter actually supports.
        int level = Math.Clamp(_defaultLevel + (percent - 100), _minLevel, _maxLevel);
        try { PowerControlSet(_adapterIndex, level); } catch (Exception) { /* leave hardware as-is */ }
    }

    public void RestoreDefault()
    {
        if (!Supported) return;
        try { PowerControlSet(_adapterIndex, _defaultLevel); } catch (Exception) { }
    }

    ~AmdGpuPower() { try { Destroy(); } catch (Exception) { } }
}

public static class GpuPowerFactory
{
    public static IGpuPower Create(HardwareInfo hw)
    {
        try
        {
            if (hw.GpuVendor == GpuVendor.Nvidia && hw.NvidiaSmiAvailable)
            {
                var path = HardwareProbe.FindNvidiaSmi();
                if (path is not null) return new NvidiaGpuPower(path);
            }
            else if (hw.GpuVendor == GpuVendor.Amd)
            {
                return new AmdGpuPower();
            }
        }
        catch (Exception) { /* fall through to unsupported */ }
        return new NullGpuPower();
    }
}
