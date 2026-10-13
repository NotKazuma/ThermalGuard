using System.Diagnostics;

namespace ThermalGuard.Startup;

/// <summary>
/// Start-with-Windows via a Task Scheduler task that runs elevated at logon. A HKCU Run key can't
/// elevate silently, so it would prompt UAC on every boot; a scheduled task with /RL HIGHEST does not.
/// </summary>
public static class AutostartManager
{
    private const string TaskName = "ThermalGuard";

    public static bool IsEnabled()
    {
        var (code, _) = Run("/Query", "/TN", TaskName);
        return code == 0;
    }

    public static bool Enable()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        var (code, err) = Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\"", "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
        if (code != 0) Trace.WriteLine($"ThermalGuard: schtasks create failed: {err}");
        return code == 0;
    }

    public static bool Disable()
    {
        var (code, _) = Run("/Delete", "/TN", TaskName, "/F");
        return code == 0 || !IsEnabled();
    }

    private static (int Code, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        return (p.ExitCode, err);
    }
}
