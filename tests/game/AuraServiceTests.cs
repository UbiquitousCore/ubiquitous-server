namespace Ubiquitous.Game.Tests;

public sealed class AuraServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tick_ReportsNothingBeforeTheFirstIntervalElapses()
    {
        var auras = new AuraService();
        auras.Apply("Creature:1", spellId: 61, perTick: 10, isHeal: false, Now);

        Assert.Empty(auras.Tick(Now + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Tick_AppliesEveryTickThenExpiresOnTheLastOne()
    {
        var auras = new AuraService();
        auras.Apply("Creature:1", spellId: 61, perTick: 10, isHeal: false, Now);
        var at = Now;

        for (int i = 0; i < AuraService.TickCount - 1; i++)
        {
            at += AuraService.TickInterval;
            var tick = Assert.Single(auras.Tick(at));
            Assert.False(tick.Expired);
            Assert.Equal(10, tick.PerTick);
        }

        at += AuraService.TickInterval;
        var last = Assert.Single(auras.Tick(at));
        Assert.True(last.Expired);

        at += AuraService.TickInterval;
        Assert.Empty(auras.Tick(at));
    }

    [Fact]
    public void Apply_RefreshesRatherThanStackingTheSameSpellOnTheSameTarget()
    {
        var auras = new AuraService();
        auras.Apply("Creature:1", spellId: 61, perTick: 10, isHeal: false, Now);
        auras.Apply("Creature:1", spellId: 61, perTick: 10, isHeal: false, Now + AuraService.TickInterval);

        var ticks = auras.Tick(Now + AuraService.TickInterval * 2);
        Assert.Single(ticks);
    }

    [Fact]
    public void Clear_DropsEveryAuraOnATargetWithNoFurtherTicks()
    {
        var auras = new AuraService();
        auras.Apply("Creature:1", spellId: 61, perTick: 10, isHeal: false, Now);
        auras.Apply("Creature:1", spellId: 62, perTick: 5, isHeal: false, Now);
        auras.Apply("Creature:2", spellId: 61, perTick: 10, isHeal: false, Now);

        auras.Clear("Creature:1");

        var ticks = auras.Tick(Now + AuraService.TickInterval);
        Assert.Single(ticks);
        Assert.Equal("Creature:2", ticks[0].TargetId);
    }

    [Fact]
    public void Tick_DistinguishesHealFromDamageAuras()
    {
        var auras = new AuraService();
        auras.Apply("player:char-a", spellId: 9, perTick: 20, isHeal: true, Now);

        var tick = Assert.Single(auras.Tick(Now + AuraService.TickInterval));
        Assert.True(tick.IsHeal);
        Assert.Equal(20, tick.PerTick);
    }

    [Fact]
    public void Tick_NamesWhoeverCastTheAura()
    {
        var auras = new AuraService();
        auras.Apply("player:b", spellId: 61, perTick: 10, isHeal: false, Now, casterId: "player:a");

        Assert.Equal("player:a", Assert.Single(auras.Tick(Now + AuraService.TickInterval)).CasterId);
    }

    [Fact]
    public void ClearWhere_EndsOnlyTheAurasItPicks()
    {
        var auras = new AuraService();
        auras.Apply("player:b", spellId: 61, perTick: 10, isHeal: false, Now, casterId: "player:a");
        auras.Apply("player:b", spellId: 62, perTick: 10, isHeal: true, Now, casterId: "player:c");

        var ended = Assert.Single(auras.ClearWhere(a => a.CasterId == "player:a"));
        Assert.Equal(61u, ended.SpellId);
        Assert.Equal(62u, Assert.Single(auras.Tick(Now + AuraService.TickInterval)).SpellId);
    }
}
