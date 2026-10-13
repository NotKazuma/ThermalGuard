using System.Diagnostics;
using System.Text.Json;

namespace ThermalGuard.Safety;

public sealed class JournalData
{
    public Guid Scheme { get; set; }
    public int OrigAc { get; set; }
    public int OrigDc { get; set; }
    public bool CapChanged { get; set; }

    public int GamePid { get; set; }
    public long GameStartTicks { get; set; }
    public ProcessPriorityClass OrigPriority { get; set; }
    public bool PriorityChanged { get; set; }
    public bool Suspended { get; set; }
    public bool GpuPowerChanged { get; set; }
}

/// <summary>
/// On-disk record of everything we changed, written before the change is made. Anything left over
/// after a crash, kill or power loss is undone by <see cref="Recover"/>.
/// </summary>
public sealed class RestoreJournal
{
    private readonly string _path;
    public RestoreJournal(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ThermalGuard", "journal.json");

    public bool Exists => File.Exists(_path);

    public void Save(JournalData d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d));
        File.Move(tmp, _path, overwrite: true); // atomic replace: never a half-written journal
    }

    public JournalData? Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<JournalData>(File.ReadAllText(_path)) : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public void Delete()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    /// <summary>
    /// Undoes whatever the journal says is still changed. Returns true if anything was restored.
    /// <paramref name="gpu"/> is optional: when recovering in a fresh process (the watchdog, or a
    /// restart) that never attached a live <see cref="IGpuPower"/>, one is probed fresh here.
    /// </summary>
    public bool Recover(IPowerApi power, IProcessOps proc, IGpuPower? gpu = null)
    {
        var d = Load();
        if (d is null) return false;

        // Resume first: a game frozen forever is the worst outcome.
        if (GameStillSame(d, proc))
        {
            if (d.Suspended) TryDo(() => proc.Resume(d.GamePid));
            if (d.PriorityChanged) TryDo(() => proc.SetPriority(d.GamePid, d.OrigPriority));
        }
        if (d.CapChanged)
        {
            TryDo(() =>
            {
                power.SetCap(d.Scheme, true, d.OrigAc);
                power.SetCap(d.Scheme, false, d.OrigDc);
                if (power.GetActiveScheme() == d.Scheme) power.ApplyScheme(d.Scheme);
            });
        }
        if (d.GpuPowerChanged)
        {
            TryDo(() =>
            {
                var g = gpu ?? GpuPowerFactory.Create(ThermalGuard.Hardware.HardwareProbe.Detect());
                g.RestoreDefault();
            });
        }
        Delete();
        return true;
    }

    private static bool GameStillSame(JournalData d, IProcessOps proc) =>
        d.GamePid != 0 && proc.GetStartTicks(d.GamePid) == d.GameStartTicks;

    private static void TryDo(Action a)
    {
        try { a(); } catch (Exception) { /* process gone or access denied: nothing more to undo */ }
    }
}
