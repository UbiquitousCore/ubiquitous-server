namespace Ubiquitous.Game.Tests;

public sealed class PeriodicTriggerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tick_ReportsNothingBeforeTheFirstIntervalElapses()
    {
        var triggers = new PeriodicTriggerService();
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);

        Assert.Empty(triggers.Tick(Now + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Tick_AppliesEveryTickThenExpiresOnTheLastOne()
    {
        var triggers = new PeriodicTriggerService();
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);
        var at = Now;

        for (int i = 0; i < AuraService.TickCount - 1; i++)
        {
            at += AuraService.TickInterval;
            var tick = Assert.Single(triggers.Tick(at));
            Assert.False(tick.Expired);
            Assert.Equal(2679u, tick.TriggerSpellId);
            Assert.Equal(100, tick.ChancePercent);
        }

        at += AuraService.TickInterval;
        var last = Assert.Single(triggers.Tick(at));
        Assert.True(last.Expired);

        at += AuraService.TickInterval;
        Assert.Empty(triggers.Tick(at));
    }

    [Fact]
    public void Apply_RefreshesRatherThanStackingTheSameSpellOnTheSameTarget()
    {
        var triggers = new PeriodicTriggerService();
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now + AuraService.TickInterval);

        var ticks = triggers.Tick(Now + AuraService.TickInterval * 2);
        Assert.Single(ticks);
    }

    [Fact]
    public void Clear_DropsEveryPeriodicTriggerOnATargetWithNoFurtherTicks()
    {
        var triggers = new PeriodicTriggerService();
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);
        triggers.Apply("Creature:2", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);

        triggers.Clear("Creature:1");

        var ticks = triggers.Tick(Now + AuraService.TickInterval);
        Assert.Single(ticks);
        Assert.Equal("Creature:2", ticks[0].TargetId);
    }

    [Fact]
    public void Tick_ReportsTheOriginalSpellIdSeparatelyFromTheTriggeredOne()
    {
        var triggers = new PeriodicTriggerService();
        triggers.Apply("Creature:1", spellId: 3736, triggerSpellId: 2679, chancePercent: 100, Now);

        var tick = Assert.Single(triggers.Tick(Now + AuraService.TickInterval));
        Assert.Equal(3736u, tick.SpellId);
        Assert.Equal(2679u, tick.TriggerSpellId);
    }
}
