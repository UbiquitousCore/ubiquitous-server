namespace Ubiquitous.Game;

public sealed record ActivePeriodicTrigger(
    string TargetId, uint SpellId, uint TriggerSpellId, int ChancePercent, int TicksRemaining, DateTimeOffset NextTickAt)
    : UnitEffect(TargetId, SpellId, null)
{
    public override bool Harmful => true;
}

public sealed record PeriodicTriggerTick(string TargetId, uint SpellId, uint TriggerSpellId, int ChancePercent, bool Expired);

public sealed class PeriodicTriggerService(UnitEffects effects, EffectKind kind)
{
    public void Apply(
        string targetId, uint spellId, uint triggerSpellId, int chancePercent, DateTimeOffset now,
        int ticks = AuraService.TickCount)
    {
        effects.RemoveWhere<ActivePeriodicTrigger>(kind, a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId);
        effects.Add(new ActivePeriodicTrigger(
            targetId, spellId, triggerSpellId, chancePercent, ticks, now + AuraService.TickInterval) { Kind = kind });
    }

    // A spell with no duration pulses until something ends it.
    public static int TicksFor(int durationMs) => durationMs <= 0
        ? int.MaxValue
        : Math.Max(1, (int)(TimeSpan.FromMilliseconds(durationMs) / AuraService.TickInterval));

    public bool End(string targetId, uint spellId) =>
        effects.RemoveWhere<ActivePeriodicTrigger>(kind, a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId).Count > 0;

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActivePeriodicTrigger>(kind, targetId).Select(a => a.SpellId)];

    public IReadOnlyList<PeriodicTriggerTick> Tick(DateTimeOffset now)
    {
        var held = effects.Of<ActivePeriodicTrigger>(kind);
        var ticks = new List<PeriodicTriggerTick>();
        for (int i = held.Count - 1; i >= 0; i--)
        {
            var trigger = held[i];
            if (now < trigger.NextTickAt) continue;

            bool expiring = trigger.TicksRemaining <= 1;
            ticks.Add(new PeriodicTriggerTick(
                trigger.TargetId, trigger.SpellId, trigger.TriggerSpellId, trigger.ChancePercent, expiring));
            if (expiring)
            {
                effects.Remove(trigger);
            }
            else
            {
                effects.Replace(trigger, trigger with
                {
                    TicksRemaining = trigger.TicksRemaining - 1,
                    NextTickAt = now + AuraService.TickInterval,
                });
            }
        }

        return ticks;
    }
}
