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

    [Fact]
    public void AWatcherIsToldWhatIsPutOnAndTakenOff_ButNotATick()
    {
        var told = new List<(uint SpellId, bool Applied)>();
        var effects = new UnitEffects((effect, applied) => told.Add((effect.SpellId, applied)));
        var auras = new AuraService(effects);
        var slow = new SlowEffectService(effects);

        auras.Apply("Creature:1", 1, 10, isHeal: false, Now);
        slow.Apply("Creature:1", 2, 40, 5000, Now);
        slow.Apply("Creature:1", 3, 40, 5000, Now);
        auras.Tick(Now + AuraService.TickInterval);
        effects.Clear("Creature:1");

        Assert.Equal([(1u, true), (2u, true), (2u, false), (3u, true), (1u, false), (3u, false)], told);
    }

    [Fact]
    public void AShieldRaisedAgain_IsToldAsTheOldOneEndingAndTheNewOneBeginning()
    {
        var told = new List<(uint SpellId, bool Applied)>();
        var effects = new UnitEffects((effect, applied) => told.Add((effect.SpellId, applied)));
        var shields = new AbsorbEffectService(effects);

        shields.Apply("player:1", 1, 50, 100, 5000, Now);
        shields.Absorb("player:1", 10, Now);
        shields.Apply("player:1", 2, 50, 100, 5000, Now);

        Assert.Equal([(1u, true), (1u, false), (2u, true)], told);
    }

    [Fact]
    public void Held_GivesWhatIsLeftByNow_AndNothingForWhatLastsUntilTakenOff()
    {
        var effects = new UnitEffects();
        new SlowEffectService(effects).Apply("Creature:1", 1, 40, 5000, Now, casterId: "player:1");
        effects.Add(new ShownAura("player:1", 2, "player:1") { Kind = EffectKind.Mount, AppliedAt = Now });

        var later = Now + TimeSpan.FromMilliseconds(1500);
        var slow = Assert.Single(effects.On("Creature:1")).Held(later);
        var mount = Assert.Single(effects.On("player:1")).Held(later);

        Assert.Equal(new HeldAura("Creature:1", "player:1", 1, 3500, 5000, Harmful: true), slow);
        Assert.Equal(new HeldAura("player:1", "player:1", 2, 0, 0, Harmful: false), mount);
    }

    [Fact]
    public void LosingSightOfACreature_DropsWhatItWears_ButItsPulseRunsOn()
    {
        var effects = new UnitEffects();
        new SlowEffectService(effects).Apply("Creature:1", 1, 40, 5000, Now);
        effects.Add(new ShownAura("Creature:1", 2, "Creature:1") { Kind = EffectKind.CreatureAura, AppliedAt = Now });
        new PeriodicTriggerService(effects, EffectKind.CreaturePulse).Apply("Creature:1", 2, 99, 100, Now);

        Assert.Equal([1u], effects.Clear("Creature:1").Select(effect => effect.SpellId));
        Assert.Equal([EffectKind.CreatureAura], effects.Forget("Creature:1").Select(effect => effect.Kind));
        Assert.Equal(EffectKind.CreaturePulse, Assert.Single(effects.On("Creature:1")).Kind);
    }
}
