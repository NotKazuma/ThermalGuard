using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ThermalGuard.Detection;

/// <summary>Win32 facts for the detector: running processes, and the foreground app if it is fullscreen.</summary>
public static class WindowProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int max);

    public static List<RunningProc> Running() =>
        Process.GetProcesses().Select(p =>
        {
            try { return new RunningProc(p.Id, p.ProcessName); } finally { p.Dispose(); }
        }).ToList();

    /// <summary>The foreground process if its window covers its whole monitor (borderless or exclusive fullscreen).</summary>
    public static RunningProc? FullscreenForeground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;

        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW") return null; // the desktop itself

        if (!GetWindowRect(h, out var w)) return null;
        var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2 /* nearest */), ref mi)) return null;
        var m = mi.Monitor;
        if (w.L > m.L || w.T > m.T || w.R < m.R || w.B < m.B) return null;

        GetWindowThreadProcessId(h, out var pid);
        try { using var p = Process.GetProcessById((int)pid); return new RunningProc(p.Id, p.ProcessName); }
        catch (ArgumentException) { return null; }
    }
}
