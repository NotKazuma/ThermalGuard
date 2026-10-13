using ThermalGuard.Core;

namespace ThermalGuard.Logging;

/// <summary>CSV temperature log, one file per day, capped by keeping only the newest files.</summary>
public sealed class CsvLogger
{
    private readonly string _dir;
    private readonly int _keepDays;

    public CsvLogger(string dir, int keepDays = 14)
    {
        _dir = dir; _keepDays = keepDays;
        Directory.CreateDirectory(dir);
        Prune();
    }

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThermalGuard", "logs");

    public void Write(DateTime now, string game, int? gpu, int? cpu, Level level, int cap)
    {
        var path = Path.Combine(_dir, $"thermal-{now:yyyy-MM-dd}.csv");
        bool isNew = !File.Exists(path);
        using var w = new StreamWriter(path, append: true);
        if (isNew) w.WriteLine("time,game,gpu_c,cpu_c,state,cpu_cap");
        w.WriteLine($"{now:s},{game},{gpu},{cpu},{level.ToString().ToUpperInvariant()},{cap}");
    }

    private void Prune()
    {
        foreach (var f in Directory.GetFiles(_dir, "thermal-*.csv"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-_keepDays))
                try { File.Delete(f); } catch (IOException) { }
    }
}
