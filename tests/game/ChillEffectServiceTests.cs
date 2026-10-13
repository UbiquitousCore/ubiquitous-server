namespace Ubiquitous.Game.Tests;

public sealed class ChillEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsChilled_HoldsForTheGivenDurationThenExpires()
    {
        var chill = new ChillEffectService();
        chill.Apply("player:1", spellId: 117, durationMs: 2000, Now);

        Assert.True(chill.IsChilled("player:1", Now + TimeSpan.FromMilliseconds(1999)));
        Assert.False(chill.IsChilled("player:1", Now + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var chill = new ChillEffectService();
        chill.Apply("player:1", spellId: 117, durationMs: 0, Now);

        Assert.True(chill.IsChilled("player:1", Now + ChillEffectService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.False(chill.IsChilled("player:1", Now + ChillEffectService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondChillOnTheSameTarget()
    {
        var chill = new ChillEffectService();
        chill.Apply("player:1", spellId: 117, durationMs: 5000, Now);
        chill.Apply("player:1", spellId: 118, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        Assert.False(chill.IsChilled("player:1", Now + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void IsChilled_IsFalseWithNothingActive()
    {
        var chill = new ChillEffectService();

        Assert.False(chill.IsChilled("player:1", Now));
    }

    [Fact]
    public void Clear_DropsTheTagWithNoFurtherEffect()
    {
        var chill = new ChillEffectService();
        chill.Apply("player:1", spellId: 117, durationMs: 5000, Now);

        chill.Clear("player:1");

        Assert.False(chill.IsChilled("player:1", Now));
    }

    [Fact]
    public void TickExpired_ReturnsAndRemovesOnlyWhatHasActuallyExpired()
    {
        var chill = new ChillEffectService();
        chill.Apply("player:1", spellId: 117, durationMs: 1000, Now);
        chill.Apply("player:2", spellId: 117, durationMs: 5000, Now);

        var expired = chill.TickExpired(Now + TimeSpan.FromMilliseconds(1000));

        var effect = Assert.Single(expired);
        Assert.Equal("player:1", effect.TargetId);
        Assert.Empty(chill.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));
        Assert.True(chill.IsChilled("player:2", Now + TimeSpan.FromMilliseconds(1000)));
    }
}
