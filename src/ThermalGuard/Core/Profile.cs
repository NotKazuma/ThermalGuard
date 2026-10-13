namespace ThermalGuard.Core;

public enum Level { Ok = 0, Warm = 1, Hot = 2, Critical = 3 }

public sealed class Profile
{
    public string DisplayName { get; set; } = "Default";
    public string ExeName { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public int GpuWarm { get; set; } = 78;
    public int GpuHot { get; set; } = 83;
    public int GpuCritical { get; set; } = 87;
    public int CpuWarm { get; set; } = 85;
    public int CpuHot { get; set; } = 92;
    public int CpuCritical { get; set; } = 96;

    // CPU max-performance cap (percent) applied at Warm/Hot/Critical.
    public int CapWarm { get; set; } = 80;
    public int CapHot { get; set; } = 50;
    public int CapCritical { get; set; } = 30;

    // GPU power limit (percent of the GPU's own default/rated limit), applied the same way as the CPU
    // cap. This is the lever that actually matters for a GPU-bound game - capping the CPU alone does
    // nothing to GPU heat. Silently skipped when no supported GPU control is available (see GpuPower.cs).
    public int GpuPowerCapWarm { get; set; } = 100;
    public int GpuPowerCapHot { get; set; } = 85;
    public int GpuPowerCapCritical { get; set; } = 70;

    // Lower the game's priority at Hot and above.
    public bool LowerPriority { get; set; } = true;

    // Briefly suspend the game at Critical. Off by default: online games disconnect.
    public bool SuspendAtCritical { get; set; } = false;
    public int SuspendMs { get; set; } = 1500;

    // Protected (anti-cheat) games: only the system-wide power caps (CPU + GPU) are used.
    public bool ProtectedGame { get; set; } = false;

    public int PollSeconds { get; set; } = 2;
    public int Cooldown { get; set; } = 5;

    public int CapFor(Level l) => l switch
    {
        Level.Warm => CapWarm,
        Level.Hot => CapHot,
        Level.Critical => CapCritical,
        _ => 100,
    };

    public int GpuPowerCapFor(Level l) => l switch
    {
        Level.Warm => GpuPowerCapWarm,
        Level.Hot => GpuPowerCapHot,
        Level.Critical => GpuPowerCapCritical,
        _ => 100,
    };
}
