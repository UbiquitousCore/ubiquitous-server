namespace Ubiquitous.Game.Tests;

public sealed class SpeedupEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentPercent_HoldsForTheGivenDurationThenExpires()
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: 20, durationMs: 2000, Now);

        Assert.Equal(20, speedup.CurrentPercent("player:1", Now + TimeSpan.FromMilliseconds(1999)));
        Assert.Equal(0, speedup.CurrentPercent("player:1", Now + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: 20, durationMs: 0, Now);

        Assert.Equal(20, speedup.CurrentPercent("player:1", Now + SpeedupEffectService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(0, speedup.CurrentPercent("player:1", Now + SpeedupEffectService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondSpeedupOnTheSameTarget()
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: 20, durationMs: 5000, Now);
        speedup.Apply("player:1", spellId: 653, percent: 100, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        Assert.Equal(0, speedup.CurrentPercent("player:1", Now + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void CurrentPercent_IsZeroWithNothingActive()
    {
        var speedup = new SpeedupEffectService();

        Assert.Equal(0, speedup.CurrentPercent("player:1", Now));
    }

    [Fact]
    public void Clear_DropsTheSpeedupWithNoFurtherEffect()
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: 20, durationMs: 5000, Now);

        speedup.Clear("player:1");

        Assert.Equal(0, speedup.CurrentPercent("player:1", Now));
    }

    [Fact]
    public void TickExpired_ReturnsAndRemovesOnlyWhatHasActuallyExpired()
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: 20, durationMs: 1000, Now);
        speedup.Apply("player:2", spellId: 170, percent: 20, durationMs: 5000, Now);

        var expired = speedup.TickExpired(Now + TimeSpan.FromMilliseconds(1000));

        var effect = Assert.Single(expired);
        Assert.Equal("player:1", effect.TargetId);
        Assert.Equal(20, effect.Percent);
        Assert.Empty(speedup.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));
        Assert.Equal(20, speedup.CurrentPercent("player:2", Now + TimeSpan.FromMilliseconds(1000)));
    }

    [Theory]
    [InlineData(500, 500)]
    [InlineData(-100, -100)]
    public void CurrentPercent_CarriesTheSignedValueWithoutClamping(int applied, int expected)
    {
        var speedup = new SpeedupEffectService();
        speedup.Apply("player:1", spellId: 170, percent: applied, durationMs: 5000, Now);

        Assert.Equal(expected, speedup.CurrentPercent("player:1", Now));
    }
}
