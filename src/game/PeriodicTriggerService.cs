namespace Ubiquitous.Game;

public sealed record ActivePeriodicTrigger(
    string TargetId, uint SpellId, uint TriggerSpellId, int ChancePercent, int TicksRemaining, DateTimeOffset NextTickAt);

public sealed record PeriodicTriggerTick(string TargetId, uint SpellId, uint TriggerSpellId, int ChancePercent, bool Expired);

public sealed class PeriodicTriggerService
{
    private readonly List<ActivePeriodicTrigger> _active = new();

    public void Apply(
        string targetId, uint spellId, uint triggerSpellId, int chancePercent, DateTimeOffset now,
        int ticks = AuraService.TickCount)
    {
        _active.RemoveAll(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId);
        _active.Add(new ActivePeriodicTrigger(
            targetId, spellId, triggerSpellId, chancePercent, ticks, now + AuraService.TickInterval));
    }

    // A spell with no duration pulses until something ends it.
    public static int TicksFor(int durationMs) => durationMs <= 0
        ? int.MaxValue
        : Math.Max(1, (int)(TimeSpan.FromMilliseconds(durationMs) / AuraService.TickInterval));

    public bool End(string targetId, uint spellId) =>
        _active.RemoveAll(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId) > 0;

    public IReadOnlyList<uint> Clear(string targetId)
    {
        var removed = _active.Where(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal))
            .Select(a => a.SpellId).ToArray();
        _active.RemoveAll(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal));
        return removed;
    }

    public IReadOnlyList<PeriodicTriggerTick> Tick(DateTimeOffset now)
    {
        var ticks = new List<PeriodicTriggerTick>();
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var trigger = _active[i];
            if (now < trigger.NextTickAt) continue;

            bool expiring = trigger.TicksRemaining <= 1;
            ticks.Add(new PeriodicTriggerTick(
                trigger.TargetId, trigger.SpellId, trigger.TriggerSpellId, trigger.ChancePercent, expiring));
            if (expiring)
            {
                _active.RemoveAt(i);
            }
            else
            {
                _active[i] = trigger with
                {
                    TicksRemaining = trigger.TicksRemaining - 1,
                    NextTickAt = now + AuraService.TickInterval,
                };
            }
        }

        return ticks;
    }
}
