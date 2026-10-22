namespace Ubiquitous.Scripts;

public static class BossTiming
{
    public const double SummonReach = 20.0;

    public static readonly TimeSpan CoverHolds = TimeSpan.FromSeconds(11);

    public static bool Ready(CombatTurnContext context, uint spellId, TimeSpan every) =>
        !context.SinceCast.TryGetValue(spellId, out var since) || since >= every;

    public static bool Due(CombatTurnContext context, uint spellId, TimeSpan first, TimeSpan then)
    {
        if (!context.SinceCast.TryGetValue(spellId, out var since)) return context.TimeInFight >= first;
        return since >= then;
    }

    public static bool SummonDue(CombatTurnContext context, uint creatureEntryId, TimeSpan every) =>
        Due(context, SummonKey(creatureEntryId), every, every);

    public static uint SummonKey(uint creatureEntryId) => SummonKeyBase + creatureEntryId;

    public const uint SummonKeyBase = 1_000_000;

    public static int Count(CombatTurnContext context, uint spellId) =>
        context.CastCount.TryGetValue(spellId, out var count) ? count : 0;

    public static TimeSpan? SinceAny(CombatTurnContext context, params uint[] spellIds)
    {
        TimeSpan? earliest = null;
        foreach (var spellId in spellIds)
        {
            if (!context.SinceCast.TryGetValue(spellId, out var since)) continue;
            if (earliest is null || since < earliest) earliest = since;
        }

        return earliest;
    }

    public static bool SharedDue(CombatTurnContext context, TimeSpan first, TimeSpan then, params uint[] spellIds)
    {
        var since = SinceAny(context, spellIds);
        return since is null ? context.TimeInFight >= first : since >= then;
    }
}
