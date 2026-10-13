namespace Ubiquitous.Game.Tests;

public sealed class ControlEffectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsControlled_HoldsForTheGivenDurationThenExpires()
    {
        var control = new ControlEffectService();
        control.Apply("Creature:1", spellId: 61, durationMs: 2000, Now);

        Assert.True(control.IsControlled("Creature:1", Now + TimeSpan.FromMilliseconds(1999)));
        Assert.False(control.IsControlled("Creature:1", Now + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var control = new ControlEffectService();
        control.Apply("Creature:1", spellId: 61, durationMs: 0, Now);

        Assert.True(control.IsControlled("Creature:1", Now + ControlEffectService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.False(control.IsControlled("Creature:1", Now + ControlEffectService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondLockOnTheSameTarget()
    {
        var control = new ControlEffectService();
        control.Apply("Creature:1", spellId: 61, durationMs: 5000, Now);
        control.Apply("Creature:1", spellId: 62, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        Assert.False(control.IsControlled("Creature:1", Now + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Clear_DropsTheLockWithNoFurtherEffect()
    {
        var control = new ControlEffectService();
        control.Apply("Creature:1", spellId: 61, durationMs: 5000, Now);

        control.Clear("Creature:1");

        Assert.False(control.IsControlled("Creature:1", Now));
    }

    [Fact]
    public void TickExpired_ReturnsAndRemovesOnlyWhatHasActuallyExpired()
    {
        var control = new ControlEffectService();
        control.Apply("Creature:1", spellId: 61, durationMs: 1000, Now);
        control.Apply("Creature:2", spellId: 61, durationMs: 5000, Now);

        var expired = control.TickExpired(Now + TimeSpan.FromMilliseconds(1000));

        var effect = Assert.Single(expired);
        Assert.Equal("Creature:1", effect.TargetId);
        Assert.Empty(control.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));
        Assert.True(control.IsControlled("Creature:2", Now + TimeSpan.FromMilliseconds(1000)));
    }
}
