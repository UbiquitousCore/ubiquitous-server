using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Game.Tests;

public sealed class SpellFlightServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SpellDefinition Bolt(float speed) => new(1, "spell-1", 0, 0, 30f, EffectDamage: 10, Speed: speed);

    [Fact]
    public void TakeArrived_HoldsASpellUntilItHasCoveredTheDistanceAtItsSpeed()
    {
        var flights = new SpellFlightService();
        flights.Launch(1, Bolt(20f), "Creature:1", distance: 10, Now);

        Assert.Empty(flights.TakeArrived(Now.AddMilliseconds(499)));
        var arrived = Assert.Single(flights.TakeArrived(Now.AddMilliseconds(500)));
        Assert.Equal("Creature:1", arrived.TargetId);
        Assert.Empty(flights.TakeArrived(Now.AddSeconds(5)));
    }

    [Fact]
    public void Flies_OnlyASpellWithASpeedThatIsNotAWeaponShotOrALeap()
    {
        Assert.True(SpellFlightService.Flies(Bolt(20f)));
        Assert.False(SpellFlightService.Flies(Bolt(0f)));
        Assert.False(SpellFlightService.Flies(Bolt(20f) with { EffectRangedWeaponHit = true }));
        Assert.False(SpellFlightService.Flies(Bolt(20f) with { EffectLeapToTarget = true }));
    }
}
