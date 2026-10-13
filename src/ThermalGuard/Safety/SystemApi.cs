using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThermalGuard.Safety;

public sealed class SystemPowerApi : IPowerApi
{
    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);

    public Guid GetActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var p));
        try { return Marshal.PtrToStructure<Guid>(p); } finally { LocalFree(p); }
    }

    public int GetCap(Guid scheme, bool ac)
    {
        var sub = SubProcessor; var set = ProcThrottleMax;
        Check(ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out var v)
                 : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out v));
        return (int)v;
    }

    public void SetCap(Guid scheme, bool ac, int percent)
    {
        var sub = SubProcessor; var set = ProcThrottleMax;
        uint v = (uint)Math.Clamp(percent, 1, 100);
        Check(ac ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, v)
                 : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, v));
    }

    public void ApplyScheme(Guid scheme) => Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));

    private static void Check(uint rc) { if (rc != 0) throw new Win32Exception((int)rc); }
}

public sealed class SystemProcessOps : IProcessOps
{
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);

    public long? GetStartTicks(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited ? null : p.StartTime.ToUniversalTime().Ticks; }
        catch (ArgumentException) { return null; }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public ProcessPriorityClass GetPriority(int pid) { using var p = Process.GetProcessById(pid); return p.PriorityClass; }
    public void SetPriority(int pid, ProcessPriorityClass c) { using var p = Process.GetProcessById(pid); p.PriorityClass = c; }
    public void Suspend(int pid) { using var p = Process.GetProcessById(pid); NtSuspendProcess(p.Handle); }
    public void Resume(int pid) { using var p = Process.GetProcessById(pid); NtResumeProcess(p.Handle); }
}
