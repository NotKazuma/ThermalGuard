namespace ThermalGuard.Core;

/// <summary>Pure state machine: temperatures in, level out. Port of Level-For plus the cooldown logic.</summary>
public sealed class ThrottleEngine
{
    private readonly Profile _p;
    public Level Current { get; private set; } = Level.Ok;

    public ThrottleEngine(Profile profile) => _p = profile;

    public Level LevelFor(int? gpu, int? cpu, int margin)
    {
        var l = Level.Ok;
        if (gpu is int g)
            l = Max(l, Classify(g, _p.GpuWarm - margin, _p.GpuHot - margin, _p.GpuCritical - margin));
        if (cpu is int c)
            l = Max(l, Classify(c, _p.CpuWarm - margin, _p.CpuHot - margin, _p.CpuCritical - margin));
        return l;
    }

    /// <summary>Feeds one reading; returns the new level (may equal the previous one).</summary>
    public Level Update(int? gpu, int? cpu)
    {
        var next = LevelFor(gpu, cpu, 0);
        if (next < Current)
        {
            // Only step down once temps are Cooldown degrees below the threshold of the current level.
            var held = LevelFor(gpu, cpu, _p.Cooldown);
            next = Max(next, (Level)Math.Min((int)Current, (int)held));
        }
        Current = next;
        return Current;
    }

    public void Reset() => Current = Level.Ok;

    private static Level Classify(int t, int warm, int hot, int critical) =>
        t >= critical ? Level.Critical : t >= hot ? Level.Hot : t >= warm ? Level.Warm : Level.Ok;

    private static Level Max(Level a, Level b) => a > b ? a : b;
}
