using System.Diagnostics;
using ThermalGuard.Core;

namespace ThermalGuard.Safety;

/// <summary>
/// Applies a throttle level to the system and one game process. Every change is journaled first,
/// and <see cref="RestoreAll"/> undoes everything and clears the journal.
/// </summary>
public sealed class ThrottleController
{
    private readonly IPowerApi _power;
    private readonly IProcessOps _proc;
    private readonly IGpuPower _gpu;
    private readonly RestoreJournal _journal;
    private readonly JournalData _d = new();
    private Profile? _profile;
    private bool _attached;
    private DateTime _lastSuspend = DateTime.MinValue;

    public ThrottleController(IPowerApi power, IProcessOps proc, RestoreJournal journal, IGpuPower? gpu = null)
    {
        _power = power; _proc = proc; _journal = journal; _gpu = gpu ?? new NullGpuPower();
    }

    public void Attach(int pid, Profile profile)
    {
        var start = _proc.GetStartTicks(pid) ?? throw new InvalidOperationException($"pid {pid} is not running");
        _profile = profile;
        _d.Scheme = _power.GetActiveScheme();
        _d.OrigAc = _power.GetCap(_d.Scheme, true);
        _d.OrigDc = _power.GetCap(_d.Scheme, false);
        _d.GamePid = pid;
        _d.GameStartTicks = start;
        _d.OrigPriority = _proc.GetPriority(pid);
        _attached = true;
    }

    public void Apply(Level level)
    {
        if (!_attached || _profile is null) return;
        var p = _profile;

        // Power cap: system-wide, so it is used even for protected games.
        int cap = p.CapFor(level);
        int ac = Math.Min(_d.OrigAc, cap), dc = Math.Min(_d.OrigDc, cap);
        if (level == Level.Ok) { ac = _d.OrigAc; dc = _d.OrigDc; }
        if (level != Level.Ok && !_d.CapChanged) { _d.CapChanged = true; _journal.Save(_d); }
        _power.SetCap(_d.Scheme, true, ac);
        _power.SetCap(_d.Scheme, false, dc);
        if (_power.GetActiveScheme() == _d.Scheme) _power.ApplyScheme(_d.Scheme);

        // GPU power limit: also system-wide (doesn't touch the game process), so it applies even for
        // protected games - it's the lever that actually matters for a GPU-bound game, which the CPU
        // cap alone does not touch.
        if (_gpu.Supported)
        {
            int gpuPct = p.GpuPowerCapFor(level);
            if (level != Level.Ok && !_d.GpuPowerChanged) { _d.GpuPowerChanged = true; _journal.Save(_d); }
            if (level == Level.Ok) _gpu.RestoreDefault(); else _gpu.SetLimitPercent(gpuPct);
        }

        // Process-touching actions are skipped entirely for protected games.
        if (p.ProtectedGame) return;

        bool wantLow = p.LowerPriority && level >= Level.Hot;
        if (wantLow && !_d.PriorityChanged) { _d.PriorityChanged = true; _journal.Save(_d); }
        if (_d.PriorityChanged)
            _proc.SetPriority(_d.GamePid, wantLow ? ProcessPriorityClass.BelowNormal : _d.OrigPriority);
    }

    /// <summary>Briefly freezes the game. The journal is written first, and the resume always runs.</summary>
    public void PulseSuspend(Action<int> sleepMs)
    {
        if (!_attached || _profile is null || _profile.ProtectedGame || !_profile.SuspendAtCritical) return;
        if (DateTime.UtcNow - _lastSuspend < TimeSpan.FromSeconds(5)) return; // rate limit
        _d.Suspended = true; _journal.Save(_d);
        try { _proc.Suspend(_d.GamePid); sleepMs(_profile.SuspendMs); }
        finally
        {
            _proc.Resume(_d.GamePid);
            _d.Suspended = false; _journal.Save(_d);
            _lastSuspend = DateTime.UtcNow;
        }
    }

    public void RestoreAll()
    {
        if (!_attached) return;
        _journal.Recover(_power, _proc, _gpu); // same code path as crash recovery, so it is the one that gets tested
        _d.CapChanged = _d.PriorityChanged = _d.Suspended = _d.GpuPowerChanged = false;
        _attached = false;
    }
}
