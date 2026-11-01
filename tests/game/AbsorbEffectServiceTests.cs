namespace Ubiquitous.Game.Tests;

public sealed class AbsorbEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AFullShieldTakesWholeBlowsUntilItsAmountIsSpent()
    {
        var shields = new AbsorbEffectService();
        shields.Apply("player:1", spellId: 5118, amount: 50, percent: 100, durationMs: 300000, Now);

        Assert.Equal(0, shields.Absorb("player:1", 30, Now));
        Assert.Equal(10, shields.Absorb("player:1", 30, Now));
        Assert.Equal(30, shields.Absorb("player:1", 30, Now));
    }

    [Fact]
    public void APartialShieldTakesItsShareOfEachBlow()
    {
        var shields = new AbsorbEffectService();
        shields.Apply("player:1", spellId: 5, amount: 50, percent: 40, durationMs: 300000, Now);

        Assert.Equal(6, shields.Absorb("player:1", 10, Now));
    }

    [Fact]
    public void AShieldUsedUpEndsBrokenOnTheNextTick_AndOnePastItsDurationEndsUnbroken()
    {
        var shields = new AbsorbEffectService();
        shields.Apply("player:1", spellId: 5118, amount: 10, percent: 100, durationMs: 300000, Now);
        shields.Apply("player:2", spellId: 5118, amount: 10, percent: 100, durationMs: 2000, Now);
        shields.Absorb("player:1", 45, Now);

        var ended = shields.TickExpired(Now + TimeSpan.FromMilliseconds(1));
        var broken = Assert.Single(ended);
        Assert.Equal("player:1", broken.Shield.TargetId);
        Assert.True(broken.Broken);

        Assert.Equal(45, shields.Absorb("player:2", 45, Now + TimeSpan.FromMilliseconds(2000)));
        var timedOut = Assert.Single(shields.TickExpired(Now + TimeSpan.FromMilliseconds(2000)));
        Assert.Equal("player:2", timedOut.Shield.TargetId);
        Assert.False(timedOut.Broken);
        Assert.Empty(shields.TickExpired(Now + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ASecondShieldReplacesTheFirst()
    {
        var shields = new AbsorbEffectService();
        shields.Apply("player:1", spellId: 5118, amount: 100, percent: 100, durationMs: 300000, Now);
        shields.Apply("player:1", spellId: 5130, amount: 5, percent: 100, durationMs: 300000, Now);

        Assert.Equal(15, shields.Absorb("player:1", 20, Now));
    }

    [Fact]
    public void NoShieldLetsTheWholeBlowThrough()
    {
        Assert.Equal(20, new AbsorbEffectService().Absorb("player:1", 20, Now));
    }
}
