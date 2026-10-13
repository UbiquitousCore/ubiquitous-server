namespace Ubiquitous.Game.Tests;

public sealed class SlowEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentPercent_HoldsForTheGivenDurationThenExpires()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 2000, Now);

        Assert.Equal(50, slow.CurrentPercent("player:1", Now + TimeSpan.FromMilliseconds(1999)));
        Assert.Equal(0, slow.CurrentPercent("player:1", Now + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 0, Now);

        Assert.Equal(50, slow.CurrentPercent("player:1", Now + SlowEffectService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(0, slow.CurrentPercent("player:1", Now + SlowEffectService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondSlowOnTheSameTarget()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 5000, Now);
        slow.Apply("player:1", spellId: 42, percent: 30, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        Assert.Equal(0, slow.CurrentPercent("player:1", Now + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void CurrentPercent_IsZeroWithNothingActive()
    {
        var slow = new SlowEffectService();

        Assert.Equal(0, slow.CurrentPercent("player:1", Now));
    }

    [Fact]
    public void Clear_DropsTheSlowWithNoFurtherEffect()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 5000, Now);

        slow.Clear("player:1");

        Assert.Equal(0, slow.CurrentPercent("player:1", Now));
    }

    [Fact]
    public void TickExpired_ReturnsAndRemovesOnlyWhatHasActuallyExpired()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 1000, Now);
        slow.Apply("player:2", spellId: 41, percent: 50, durationMs: 5000, Now);

        var expired = slow.TickExpired(Now + TimeSpan.FromMilliseconds(1000));

        var effect = Assert.Single(expired);
        Assert.Equal("player:1", effect.TargetId);
        Assert.Equal(50, effect.Percent);
        Assert.Empty(slow.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));
        Assert.Equal(50, slow.CurrentPercent("player:2", Now + TimeSpan.FromMilliseconds(1000)));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void CurrentPercent_ClampsToTheValidRange(int applied, int expected)
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: applied, durationMs: 5000, Now);

        Assert.Equal(expected, slow.CurrentPercent("player:1", Now));
    }

    [Fact]
    public void ClearWhere_EndsASlowByItsCasterAndReportsIt()
    {
        var slow = new SlowEffectService();
        slow.Apply("player:1", spellId: 41, percent: 50, durationMs: 2000, Now, casterId: "player:2");

        var ended = Assert.Single(slow.ClearWhere(e => e.CasterId == "player:2"));
        Assert.Equal(41u, ended.SpellId);
        Assert.Equal(0, slow.CurrentPercent("player:1", Now));
        Assert.Empty(slow.TickExpired(Now + TimeSpan.FromSeconds(5)));
    }
}
