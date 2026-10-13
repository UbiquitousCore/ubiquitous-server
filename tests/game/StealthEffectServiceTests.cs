namespace Ubiquitous.Game.Tests;

public sealed class StealthEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsStealthed_HoldsForSneaksFourteenSecondsThenExpires()
    {
        var stealth = new StealthEffectService();
        stealth.Apply("player:1", spellId: 5813, durationMs: 14000, Now);

        Assert.True(stealth.IsStealthed("player:1", Now + TimeSpan.FromMilliseconds(13999)));
        Assert.False(stealth.IsStealthed("player:1", Now + TimeSpan.FromMilliseconds(14000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var stealth = new StealthEffectService();
        stealth.Apply("player:1", spellId: 5813, durationMs: 0, Now);

        Assert.True(stealth.IsStealthed(
            "player:1", Now + StealthEffectService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.False(stealth.IsStealthed("player:1", Now + StealthEffectService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondStealthOnTheSameUnit()
    {
        var stealth = new StealthEffectService();
        stealth.Apply("player:1", spellId: 5813, durationMs: 14000, Now);
        stealth.Apply("player:1", spellId: 5824, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        Assert.False(stealth.IsStealthed("player:1", Now + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void IsStealthed_IsFalseWithNothingActive()
    {
        Assert.False(new StealthEffectService().IsStealthed("player:1", Now));
    }

    [Fact]
    public void Clear_DropsTheStateWithNoFurtherEffect()
    {
        var stealth = new StealthEffectService();
        stealth.Apply("player:1", spellId: 5813, durationMs: 14000, Now);

        stealth.Clear("player:1");

        Assert.False(stealth.IsStealthed("player:1", Now));
    }

    [Fact]
    public void TickExpired_ReturnsAndRemovesOnlyWhatHasActuallyExpired()
    {
        var stealth = new StealthEffectService();
        stealth.Apply("player:1", spellId: 5813, durationMs: 1000, Now);
        stealth.Apply("player:2", spellId: 5813, durationMs: 14000, Now);

        var expired = stealth.TickExpired(Now + TimeSpan.FromMilliseconds(1000));

        var effect = Assert.Single(expired);
        Assert.Equal("player:1", effect.TargetId);
        Assert.Equal(5813u, effect.SpellId);
        Assert.Empty(stealth.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));
        Assert.True(stealth.IsStealthed("player:2", Now + TimeSpan.FromMilliseconds(1000)));
    }
}
