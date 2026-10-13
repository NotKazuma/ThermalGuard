using ThermalGuard.Core;

namespace ThermalGuard.Tests;

public class ThrottleEngineTests
{
    private static ThrottleEngine Engine() => new(new Profile());

    [Theory]
    [InlineData(60, 60, Level.Ok)]
    [InlineData(78, 60, Level.Warm)]
    [InlineData(60, 92, Level.Hot)]
    [InlineData(87, 60, Level.Critical)]
    [InlineData(60, 96, Level.Critical)]
    public void Thresholds(int gpu, int cpu, Level expected) =>
        Assert.Equal(expected, Engine().LevelFor(gpu, cpu, 0));

    [Fact]
    public void NullSensorsAreIgnored()
    {
        var e = Engine();
        Assert.Equal(Level.Warm, e.LevelFor(null, 85, 0));
        Assert.Equal(Level.Hot, e.LevelFor(83, null, 0));
        Assert.Equal(Level.Ok, e.LevelFor(null, null, 0));
    }

    [Fact]
    public void CooldownHoldsLevelUntilTempsDropEnough()
    {
        var e = Engine();
        Assert.Equal(Level.Hot, e.Update(83, 60));
        // 80 is below Hot (83) but within the 5-degree cooldown band: stay Hot.
        Assert.Equal(Level.Hot, e.Update(80, 60));
        // 77 is below Hot's 83-5=78 band: step down one level, but Warm is still held (78-5=73).
        Assert.Equal(Level.Warm, e.Update(77, 60));
        Assert.Equal(Level.Warm, e.Update(74, 60));
        Assert.Equal(Level.Ok, e.Update(72, 60));
    }

    [Fact]
    public void RisesImmediately()
    {
        var e = Engine();
        Assert.Equal(Level.Critical, e.Update(90, 60));
    }

    [Theory]
    [InlineData(Level.Ok, 100)]
    [InlineData(Level.Warm, 100)]
    [InlineData(Level.Hot, 85)]
    [InlineData(Level.Critical, 70)]
    public void GpuPowerCapFor_MatchesDefaults(Level level, int expectedPercent) =>
        Assert.Equal(expectedPercent, new Profile().GpuPowerCapFor(level));
}
