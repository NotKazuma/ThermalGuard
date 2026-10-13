using System.Diagnostics;
using System.Runtime.InteropServices;
using ThermalGuard.Config;
using ThermalGuard.Core;
using ThermalGuard.Detection;
using ThermalGuard.Hardware;
using ThermalGuard.Logging;
using ThermalGuard.Safety;
using ThermalGuard.Sensors;
using ThermalGuard.UI;

namespace ThermalGuard;

static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        var power = new SystemPowerApi();
        var proc = new SystemProcessOps();

        // Background copy that restores settings if the main app dies.
        if (args.Length >= 3 && args[0] == "--watchdog")
            return Watchdog.Run(int.Parse(args[1]), args[2], power, proc);

        if (args.Length >= 1 && args[0] is "--check" or "--run")
        {
            AttachConsole(-1);
            return args[0] == "--check" ? Check(power, proc) : RunHeadless(args, power, proc);
        }

        using var mutex = new Mutex(initiallyOwned: true, "ThermalGuard-SingleInstance-7b2e", out bool created);
        if (!created)
        {
            MessageBox.Show("ThermalGuard is already running (check the system tray).", "ThermalGuard",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        var store = new ProfileStore(ProfileStore.DefaultPath);
        var cfg = store.Load();
        var journal = new RestoreJournal(RestoreJournal.DefaultPath);
        using var temps = new CompositeSource();
        var hw = temps.DetectHardwareInfo(); // reads from the Computer temps already opened - see HardwareProbe's note
        var gpu = GpuPowerFactory.Create(hw);
        var ctl = new ThrottleController(power, proc, journal, gpu);
        var log = new CsvLogger(CsvLogger.DefaultDir);
        var detector = new GameDetector(cfg);
        using var tray = new TrayForm(cfg, store, detector, temps, ctl, log, journal, hw, gpu);
        Application.Run(tray);
        return 0;
    }

    /// <summary>Read-only sanity check of the Windows calls and sensors. Changes nothing.</summary>
    static int Check(SystemPowerApi power, SystemProcessOps proc)
    {
        var s = power.GetActiveScheme();
        Console.WriteLine($"Active scheme {s}  CPU max AC={power.GetCap(s, true)}%  DC={power.GetCap(s, false)}%");
        Console.WriteLine($"Journal present: {new RestoreJournal(RestoreJournal.DefaultPath).Exists}");

        using var temps = new CompositeSource();
        if (temps.LhmError != "") Console.WriteLine($"LibreHardwareMonitor failed: {temps.LhmError}");
        var hw = temps.DetectHardwareInfo();
        Console.WriteLine($"CPU: {hw.CpuName}");
        Console.WriteLine($"GPU: {hw.GpuName}  (vendor: {hw.GpuVendor})");
        var gpu = GpuPowerFactory.Create(hw);
        Console.WriteLine($"GPU power-limit control: {(gpu.Supported ? $"supported, current limit {gpu.GetLimitW()?.ToString() ?? "n/a"} W" : "not supported on this GPU")}");

        temps.Update();
        Console.WriteLine($"CPU {temps.CpuTemp?.ToString() ?? "n/a"} C  load {temps.CpuLoad?.ToString() ?? "n/a"}%  {temps.CpuClockMHz?.ToString() ?? "n/a"} MHz");
        Console.WriteLine($"GPU {temps.GpuTemp?.ToString() ?? "n/a"} C  load {temps.GpuLoad?.ToString() ?? "n/a"}%  {temps.GpuClockMHz?.ToString() ?? "n/a"} MHz  {temps.GpuPowerW?.ToString() ?? "n/a"} W");
        return 0;
    }

    /// <summary>--run &lt;exeName&gt;: wait for the game, throttle it, restore on exit.</summary>
    static int RunHeadless(string[] args, SystemPowerApi power, SystemProcessOps proc)
    {
        if (args.Length < 2) { Console.WriteLine("usage: ThermalGuard --run <processName without .exe>"); return 2; }
        var journal = new RestoreJournal(RestoreJournal.DefaultPath);
        if (journal.Recover(power, proc)) Console.WriteLine("Recovered settings left over from a previous run.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        using var watchdog = Watchdog.Start(RestoreJournal.DefaultPath);
        using var temps = new CompositeSource();
        var gpu = GpuPowerFactory.Create(temps.DetectHardwareInfo());
        var ctl = new ThrottleController(power, proc, journal, gpu);
        var runner = new Runner(temps, ctl, new CsvLogger(CsvLogger.DefaultDir), Console.WriteLine);
        var profile = new Profile { DisplayName = args[1], ExeName = args[1] };
        if (args.Contains("--test")) // forces Hot with a visibly different cap, for the kill/restore test
        {
            profile.CpuWarm = profile.GpuWarm = 1; profile.CpuHot = profile.GpuHot = 2;
            profile.CpuCritical = profile.GpuCritical = 999; profile.CapHot = 35;
        }

        Console.WriteLine($"Waiting for {args[1]}.exe ...");
        Process? game = null;
        while (game is null && !cts.IsCancellationRequested)
        {
            game = Process.GetProcessesByName(args[1]).FirstOrDefault();
            if (game is null) cts.Token.WaitHandle.WaitOne(2000);
        }
        if (game is null) return 0;
        Console.WriteLine($"Attached to {game.ProcessName} (PID {game.Id}). Ctrl+C to stop.");
        runner.Run(game, profile, cts.Token);
        return 0;
    }
}
