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
        speedup.Apply("Creature:1", 2, 30, 1000, Now);
        melee.Apply("Creature:1", 3, 20, 5000, Now);

        Assert.Equal(40, slow.CurrentPercent("Creature:1", Now));
        Assert.Equal(30, speedup.CurrentPercent("Creature:1", Now));
        Assert.Equal(20, melee.CurrentPercent("Creature:1", Now));
        var later = Now + TimeSpan.FromSeconds(2);
        Assert.Equal([2u], speedup.TickExpired(later).Select(effect => effect.SpellId));
        Assert.Equal(20, melee.CurrentPercent("Creature:1", later));
        Assert.True(slow.IsSlowed("Creature:1", later));
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

    [Fact]
    public void Clear_EndsEverySpellEffectOnTheUnit_ButNotACreaturesOwnPulse()
    {
        var effects = new UnitEffects();
        new SlowEffectService(effects).Apply("Creature:1", 1, 40, 5000, Now);
        new AuraService(effects).Apply("Creature:1", 2, 10, isHeal: false, Now);
        new SlowEffectService(effects).Apply("Creature:2", 3, 40, 5000, Now);
        var pulses = new PeriodicTriggerService(effects, EffectKind.CreaturePulse);
        pulses.Apply("Creature:1", 4, 99, 100, Now);

        Assert.Equal([1u, 2u], effects.Clear("Creature:1").Select(effect => effect.SpellId));

        Assert.Equal([3u, 4u], [.. effects.On("Creature:2").Concat(effects.On("Creature:1")).Select(effect => effect.SpellId)]);
    }

    [Theory]
    [InlineData(EffectKind.Periodic, true, true)]
    [InlineData(EffectKind.Control, true, true)]
    [InlineData(EffectKind.Silence, true, true)]
    [InlineData(EffectKind.Slow, true, true)]
    [InlineData(EffectKind.Speedup, true, true)]
    [InlineData(EffectKind.MeleeAttackSpeed, true, true)]
    [InlineData(EffectKind.Chill, true, true)]
    [InlineData(EffectKind.AttributeModifier, true, true)]
    [InlineData(EffectKind.Absorb, false, true)]
    [InlineData(EffectKind.Avatar, false, true)]
    [InlineData(EffectKind.Stealth, false, true)]
    [InlineData(EffectKind.PeriodicTrigger, false, true)]
    [InlineData(EffectKind.RangeAttackSpeed, false, false)]
    [InlineData(EffectKind.CreaturePulse, false, false)]
    public void ACleanseReachesHarmTheOpponentLeft_ADeathReachesMore(EffectKind kind, bool cleansable, bool endsWithDeath)
    {
        Assert.Equal(cleansable, EffectKinds.Cleansable(kind));
        Assert.Equal(endsWithDeath, EffectKinds.EndsWithDeath(kind));
    }
}
