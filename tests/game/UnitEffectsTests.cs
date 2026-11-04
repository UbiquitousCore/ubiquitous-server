using Ubiquitous.Game;

namespace Ubiquitous.Game.Tests;

public sealed class UnitEffectsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KindsKeptInOneStore_EachSeeOnlyTheirOwn()
    {
        var effects = new UnitEffects();
        var slow = new SlowEffectService(effects);
        var speedup = new SpeedupEffectService(effects, EffectKind.Speedup);
        var melee = new SpeedupEffectService(effects, EffectKind.MeleeAttackSpeed);

        slow.Apply("Creature:1", 1, 40, 5000, Now);
        speedup.Apply("Creature:1", 2, 30, 5000, Now);
        melee.Apply("Creature:1", 3, 20, 5000, Now);

        Assert.Equal(40, slow.CurrentPercent("Creature:1", Now));
        Assert.Equal(30, speedup.CurrentPercent("Creature:1", Now));
        Assert.Equal(20, melee.CurrentPercent("Creature:1", Now));
        Assert.Equal([2u], speedup.Clear("Creature:1"));
        Assert.Equal(20, melee.CurrentPercent("Creature:1", Now));
        Assert.True(slow.IsSlowed("Creature:1", Now));
    }

    [Fact]
    public void ATickKeepsAnEffectWhereItWas()
    {
        var effects = new UnitEffects();
        var auras = new AuraService(effects);
        auras.Apply("Creature:1", 1, 10, isHeal: false, Now);
        auras.Apply("Creature:2", 2, 10, isHeal: false, Now);

        var first = auras.Tick(Now + AuraService.TickInterval);

        Assert.Equal(["Creature:2", "Creature:1"], first.Select(tick => tick.TargetId));
        Assert.Equal(["Creature:1", "Creature:2"],
            effects.Of<ActiveAura>(EffectKind.Periodic).Select(aura => aura.TargetId));
    }
}
