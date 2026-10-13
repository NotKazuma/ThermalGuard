using System.Diagnostics;

namespace ThermalGuard.Safety;

/// <summary>
/// Second copy of this exe (<c>--watchdog &lt;parentPid&gt;</c>). It waits for the main app to exit
/// for any reason, including a hard kill, then replays the journal.
/// </summary>
public static class Watchdog
{
    public static Process? Start(string journalPath)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return null;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--watchdog");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add(journalPath);
        return Process.Start(psi);
    }

    public static int Run(int parentPid, string journalPath, IPowerApi power, IProcessOps proc)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch (ArgumentException) { /* parent already gone */ }

        new RestoreJournal(journalPath).Recover(power, proc);
        return 0;
    }
}
