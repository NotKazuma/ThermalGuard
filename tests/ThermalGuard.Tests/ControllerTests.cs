using System.Diagnostics;
using ThermalGuard.Core;
using ThermalGuard.Safety;

namespace ThermalGuard.Tests;

internal sealed class FakePower : IPowerApi
{
    public Guid Scheme = Guid.NewGuid();
    public int Ac = 100, Dc = 90;
    public Guid GetActiveScheme() => Scheme;
    public int GetCap(Guid s, bool ac) => ac ? Ac : Dc;
    public void SetCap(Guid s, bool ac, int p) { if (ac) Ac = p; else Dc = p; }
    public int MinAc = 5, MinDc = 5;
    public int MinWrites;
    public int GetMin(Guid s, bool ac) => ac ? MinAc : MinDc;
    public void SetMin(Guid s, bool ac, int p) { MinWrites++; if (ac) MinAc = p; else MinDc = p; }
    public void ApplyScheme(Guid s) { }
}

internal sealed class FakeProc : IProcessOps
{
    public long? Start = 12345;
    public ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
    public bool Suspended;
    public long? GetStartTicks(int pid) => Start;
    public ProcessPriorityClass GetPriority(int pid) => Priority;
    public void SetPriority(int pid, ProcessPriorityClass p) => Priority = p;
    public void Suspend(int pid) => Suspended = true;
    public void Resume(int pid) => Suspended = false;
}

internal sealed class FakeGpuPower : IGpuPower
{
    public bool Supported { get; set; } = true;
    public int LastPercent = 100;
    public int? GetLimitW() => null;
    public void SetLimitPercent(int percent) => LastPercent = percent;
    public void RestoreDefault() => LastPercent = 100;
}

public sealed class ControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tg-" + Guid.NewGuid().ToString("N"));
    private readonly FakePower _power = new();
    private readonly FakeProc _proc = new();
    private readonly FakeGpuPower _gpu = new();
    private readonly RestoreJournal _journal;
    private readonly ThrottleController _c;

    public ControllerTests()
    {
        _journal = new RestoreJournal(Path.Combine(_dir, "journal.json"));
        _c = new ThrottleController(_power, _proc, _journal, _gpu);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void HotCapsAndLowersPriority_RestoreAllUndoesBoth()
    {
        _c.Attach(1, new Profile());
        _c.Apply(Level.Hot);
        Assert.Equal(50, _power.Ac);
        Assert.Equal(50, _power.Dc);
        Assert.Equal(ProcessPriorityClass.BelowNormal, _proc.Priority);

        _c.RestoreAll();
        Assert.Equal(100, _power.Ac);
        Assert.Equal(90, _power.Dc);
        Assert.Equal(ProcessPriorityClass.Normal, _proc.Priority);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void CapNeverRaisesAnAlreadyLowerUserLimit()
    {
        _power.Dc = 40;
        _c.Attach(1, new Profile());
        _c.Apply(Level.Warm); // cap 80
        Assert.Equal(40, _power.Dc);
        Assert.Equal(80, _power.Ac);
    }

    [Fact]
    public void ProtectedGame_OnlyPowerCap()
    {
        _c.Attach(1, new Profile { ProtectedGame = true, SuspendAtCritical = true });
        _c.Apply(Level.Critical);
        _c.PulseSuspend(_ => { });
        Assert.Equal(30, _power.Ac);
        Assert.Equal(ProcessPriorityClass.Normal, _proc.Priority);
        Assert.False(_proc.Suspended);
    }

    [Fact]
    public void SuspendAlwaysResumes_EvenIfSleepThrows()
    {
        _c.Attach(1, new Profile { SuspendAtCritical = true });
        Assert.Throws<InvalidOperationException>(() => _c.PulseSuspend(_ => throw new InvalidOperationException()));
        Assert.False(_proc.Suspended);
    }

    [Fact]
    public void CrashRecovery_FromJournalAlone()
    {
        _c.Attach(1, new Profile());
        _c.Apply(Level.Critical);
        // Simulate the app being killed: a fresh journal object and no controller state.
        var after = new RestoreJournal(Path.Combine(_dir, "journal.json"));
        Assert.True(after.Recover(_power, _proc));
        Assert.Equal(100, _power.Ac);
        Assert.Equal(90, _power.Dc);
        Assert.Equal(ProcessPriorityClass.Normal, _proc.Priority);
    }

    [Fact]
    public void CrashMidSuspend_RecoveryResumesGame()
    {
        _c.Attach(1, new Profile { SuspendAtCritical = true });
        _c.Apply(Level.Critical);
        _c.PulseSuspend(_ => { /* "killed" before the finally could matter */ });
        _proc.Suspended = true; // as if the resume never ran
        var d = _journal.Load()!;
        d.Suspended = true; _journal.Save(d);

        Assert.True(_journal.Recover(_power, _proc));
        Assert.False(_proc.Suspended);
    }

    [Fact]
    public void Recovery_DoesNotTouchReusedPid()
    {
        _c.Attach(1, new Profile());
        _c.Apply(Level.Hot);
        _proc.Start = 99999;                    // pid now belongs to a different process
        _proc.Priority = ProcessPriorityClass.High;
        _journal.Recover(_power, _proc);
        Assert.Equal(ProcessPriorityClass.High, _proc.Priority);
        Assert.Equal(100, _power.Ac);           // power cap is still restored
    }

    [Fact]
    public void NothingChanged_NoJournalLeftBehind()
    {
        _c.Attach(1, new Profile());
        _c.Apply(Level.Ok);
        _c.RestoreAll();
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void GpuPowerCap_AppliedAtEachLevel_RestoredOnExit()
    {
        _c.Attach(1, new Profile()); // defaults: 100/85/70
        _c.Apply(Level.Warm);
        Assert.Equal(100, _gpu.LastPercent);
        _c.Apply(Level.Hot);
        Assert.Equal(85, _gpu.LastPercent);
        _c.Apply(Level.Critical);
        Assert.Equal(70, _gpu.LastPercent);

        _c.RestoreAll();
        Assert.Equal(100, _gpu.LastPercent);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void GpuPowerCap_SkippedWhenUnsupported()
    {
        _gpu.Supported = false;
        _c.Attach(1, new Profile());
        _c.Apply(Level.Critical);
        Assert.Equal(100, _gpu.LastPercent); // never touched
    }

    [Fact]
    public void GpuPowerCap_AppliesEvenForProtectedGame()
    {
        // Unlike priority/suspend, the GPU power limit doesn't touch the game process, so it should
        // still apply for anti-cheat-protected profiles.
        _c.Attach(1, new Profile { ProtectedGame = true });
        _c.Apply(Level.Critical);
        Assert.Equal(70, _gpu.LastPercent);
    }

    [Fact]
    public void MinimumAboveTheCap_IsLoweredWithIt_AndPutBackAtOk()
    {
        // A plan that pins the minimum at 100 % (and a lower maximum): without this the minimum wins and the cap does nothing.
        _power.MinAc = 100; _power.MinDc = 100; _power.Ac = 50; _power.Dc = 50;
        _c.Attach(1, new Profile());

        _c.Apply(Level.Warm); // cap 80
        Assert.Equal(80, _power.MinAc);
        Assert.Equal(50, _power.Ac); // never raises the plan's own lower maximum
        _c.Apply(Level.Critical); // cap 30
        Assert.Equal(30, _power.MinAc);
        Assert.Equal(30, _power.Ac);

        _c.Apply(Level.Ok);
        Assert.Equal(100, _power.MinAc);
        Assert.Equal(50, _power.Ac);

        _c.RestoreAll();
        Assert.Equal(100, _power.MinDc);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void MinimumAlreadyUnderTheCap_IsNeverTouched()
    {
        _c.Attach(1, new Profile()); // fake plan minimum is 5 %
        _c.Apply(Level.Critical);
        _c.RestoreAll();
        Assert.Equal(0, _power.MinWrites);
        Assert.Equal(5, _power.MinAc);
    }

    [Fact]
    public void CrashRecovery_RestoresALoweredMinimum()
    {
        _power.MinAc = 100; _power.MinDc = 100;
        _c.Attach(1, new Profile());
        _c.Apply(Level.Hot);
        Assert.Equal(50, _power.MinAc);

        var after = new RestoreJournal(Path.Combine(_dir, "journal.json"));
        Assert.True(after.Recover(_power, _proc));
        Assert.Equal(100, _power.MinAc);
        Assert.Equal(100, _power.MinDc);
    }

    [Fact]
    public void AnOldJournalWithoutMinimumFields_StillRecoversAndLeavesTheMinimumAlone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "journal.json"),
            $"{{\"Scheme\":\"{_power.Scheme}\",\"OrigAc\":100,\"OrigDc\":90,\"CapChanged\":true}}");
        _power.Ac = 30; _power.MinAc = 77;

        Assert.True(_journal.Recover(_power, _proc));
        Assert.Equal(100, _power.Ac);
        Assert.Equal(77, _power.MinAc);
        Assert.Equal(0, _power.MinWrites);
    }

    [Fact]
    public void CrashRecovery_RestoresGpuDefaultToo()
    {
        _c.Attach(1, new Profile());
        _c.Apply(Level.Critical);
        Assert.Equal(70, _gpu.LastPercent);

        var after = new RestoreJournal(Path.Combine(_dir, "journal.json"));
        Assert.True(after.Recover(_power, _proc, _gpu));
        Assert.Equal(100, _gpu.LastPercent);
    }
}
