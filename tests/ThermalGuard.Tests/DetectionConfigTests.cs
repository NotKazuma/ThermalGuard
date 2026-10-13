using ThermalGuard.Config;
using ThermalGuard.Core;
using ThermalGuard.Detection;

namespace ThermalGuard.Tests;

public class DetectionConfigTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    private static AppConfig Cfg() => AppConfig.Seed();
    private static RunningProc P(int pid, string n) => new(pid, n);

    [Fact]
    public void ProfileMatch_IgnoresCaseAndExtension()
    {
        var d = new GameDetector(Cfg());
        var m = d.Choose(new[] { P(1, "explorer"), P(2, "acblackflag.exe") }, null, T0);
        Assert.NotNull(m);
        Assert.Equal(2, m!.Proc.Pid);
        Assert.False(m.AutoDetected);
    }

    [Fact]
    public void DisabledProfile_IsNotMatched()
    {
        var c = Cfg(); c.Profiles[0].Enabled = false; c.AutoDetect = false;
        Assert.Null(new GameDetector(c).Choose(new[] { P(2, "ACBlackFlag") }, null, T0));
    }

    [Fact]
    public void TwoProfilesRunning_ForegroundWins()
    {
        var c = Cfg(); c.Profiles.Add(new Profile { DisplayName = "B", ExeName = "gameb" });
        var m = new GameDetector(c).Choose(new[] { P(2, "ACBlackFlag"), P(3, "gameb") }, P(3, "gameb"), T0);
        Assert.Equal(3, m!.Proc.Pid);
    }

    [Fact]
    public void AutoDetect_NeedsFullscreenToHoldForConfiguredTime()
    {
        var d = new GameDetector(Cfg());
        var fg = P(9, "someunknowngame");
        Assert.Null(d.Choose(new[] { fg }, fg, T0));
        Assert.Null(d.Choose(new[] { fg }, fg, T0.AddSeconds(5)));
        var m = d.Choose(new[] { fg }, fg, T0.AddSeconds(10));
        Assert.NotNull(m);
        Assert.True(m!.AutoDetected);
        Assert.Equal("someunknowngame", m.Profile.ExeName);
    }

    [Fact]
    public void AutoDetect_TimerResetsWhenForegroundChanges()
    {
        var d = new GameDetector(Cfg());
        d.Choose(Array.Empty<RunningProc>(), P(9, "gamea"), T0);
        d.Choose(Array.Empty<RunningProc>(), P(10, "gameb"), T0.AddSeconds(8));
        Assert.Null(d.Choose(Array.Empty<RunningProc>(), P(10, "gameb"), T0.AddSeconds(12)));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("vlc.exe")]
    [InlineData("steamwebhelper")]
    public void AutoDetect_NeverPicksExcludedApps(string name)
    {
        var d = new GameDetector(Cfg());
        var fg = P(5, name);
        d.Choose(new[] { fg }, fg, T0);
        Assert.Null(d.Choose(new[] { fg }, fg, T0.AddMinutes(5)));
    }

    [Fact]
    public void UserIgnoreList_IsHonoured()
    {
        var c = Cfg(); c.Ignored.Add("MyPlayer.exe");
        var d = new GameDetector(c);
        var fg = P(5, "myplayer");
        d.Choose(new[] { fg }, fg, T0);
        Assert.Null(d.Choose(new[] { fg }, fg, T0.AddMinutes(5)));
    }

    [Fact]
    public void Store_SeedsOnFirstRun_AndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "tgcfg-" + Guid.NewGuid().ToString("N"), "config.json");
        try
        {
            var s = new ProfileStore(path);
            var c = s.Load();
            Assert.Equal("ACBlackFlag", c.Profiles[0].ExeName);
            c.Profiles[0].CpuHot = 90; s.Save(c);
            Assert.Equal(90, new ProfileStore(path).Load().Profiles[0].CpuHot);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void Store_CorruptFile_IsBackedUp_NotLost()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tgcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var c = new ProfileStore(path).Load();
            Assert.NotEmpty(c.Profiles);
            Assert.Single(Directory.GetFiles(dir, "config.json.bad-*"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
