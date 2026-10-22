namespace Ubiquitous.Scripts;

public sealed record InmateMove(
    uint Spell,
    CastTarget At,
    TimeSpan First,
    TimeSpan? Every = null,
    IReadOnlyList<double>? AtHealth = null,
    double? BelowHealth = null,
    bool InMeleeOnly = false,
    uint? ThenSelf = null,
    uint? NeedsAlly = null,
    uint? SummonEntry = null,
    bool NeedsWoundedAlly = false);

public sealed class InmateScript(string name, IReadOnlyList<InmateMove> moves, IReadOnlyList<uint>? diesInto = null)
    : CreatureScript
{
    public override string Name => name;

    public override IReadOnlyList<uint> DiesInto => diesInto ?? [];

    public override bool GivesUp(GiveUpContext context) => DungeonInmateScript.Instance.GivesUp(context);

    public override CombatAction? Act(CombatTurnContext context)
    {
        foreach (var move in moves)
        {
            if (move.ThenSelf is uint follow && BossTiming.Count(context, move.Spell) > BossTiming.Count(context, follow))
                return new CombatAction(Cast: follow, At: CastTarget.Self);
        }

        foreach (var move in moves)
        {
            if (!Ready(move, context)) continue;
            return move.SummonEntry is uint summon
                ? new CombatAction(Summon: summon)
                : new CombatAction(Cast: move.Spell, At: move.At, AllyEntry: move.NeedsAlly);
        }

        return null;
    }

    private static bool Ready(InmateMove move, CombatTurnContext context)
    {
        if (move.InMeleeOnly && !context.InMeleeRange) return false;
        if (move.BelowHealth is double below && context.HealthFraction > below) return false;
        if (move.NeedsAlly is uint ally && !context.HasAllyInReach(ally)) return false;
        if (move.NeedsWoundedAlly && !context.WoundedAllyInReach) return false;

        var key = move.SummonEntry is uint summon ? BossTiming.SummonKey(summon) : move.Spell;
        var cast = BossTiming.Count(context, key);
        if (move.AtHealth is { } steps) return cast < steps.Count(step => context.HealthFraction <= step);
        if (move.Every is not TimeSpan every) return cast == 0 && context.TimeInFight >= move.First;
        return BossTiming.Due(context, key, move.First, every);
    }
}
