namespace Ubiquitous.Scripts;

public sealed class CosmoScript : CreatureScript
{
    public static readonly CosmoScript Instance = new();

    private CosmoScript() { }

    public override string Name => "boss.cosmo";

    private const uint ThunderCurse = 2632;
    private const uint ColdCurse = 2633;
    private const uint FlameCurse = 2634;

    private const uint Metamorphosis = 2635;
    private const uint RemoveMetamorphosis = 2647;

    private const uint ShadowErode = 2637;

    private const uint DevourPuppet = 2645;
    private const uint FeedOnPuppet = 2636;

    private const uint UndeadPuppet = 2697;

    private const string Demon = "demon";
    private const string Reverted = "reverted";

    private const double ChangesAt = 0.70;
    private const double ChangesBackAt = 0.25;

    private static readonly TimeSpan BetweenCurses = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ErodeOpensAt = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErodeEvery = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PuppetEvery = TimeSpan.FromSeconds(15);

    public override CombatAction? Act(CombatTurnContext context)
    {
        var demon = context.Marks.Contains(Demon);

        if (demon && !context.Marks.Contains(Reverted) && context.HealthFraction <= ChangesBackAt)
            return new CombatAction(Cast: RemoveMetamorphosis, At: CastTarget.Self, Sets: Reverted, Clears: Demon);

        if (!demon && !context.Marks.Contains(Reverted) && context.HealthFraction <= ChangesAt)
            return new CombatAction(Cast: Metamorphosis, At: CastTarget.Self, Sets: Demon);

        return demon ? Shaped(context) : Casting(context);
    }

    private static CombatAction? Casting(CombatTurnContext context)
    {
        var thunder = BossTiming.Count(context, ThunderCurse);
        var cold = BossTiming.Count(context, ColdCurse);
        var flame = BossTiming.Count(context, FlameCurse);
        var next = thunder <= cold && thunder <= flame ? ThunderCurse
            : cold <= flame ? ColdCurse
            : FlameCurse;

        var sinceAny = BossTiming.SinceAny(context, ThunderCurse, ColdCurse, FlameCurse);
        if (sinceAny is not null && sinceAny < BetweenCurses) return null;
        return new CombatAction(Cast: next, At: CastTarget.Enemy);
    }

    private static CombatAction? Shaped(CombatTurnContext context)
    {
        if (BossTiming.Count(context, DevourPuppet) > BossTiming.Count(context, FeedOnPuppet))
            return new CombatAction(Cast: FeedOnPuppet, At: CastTarget.Self);

        if (context.SummonsInReach > 0 && BossTiming.Ready(context, DevourPuppet, BossTiming.BetweenMeals))
            return new CombatAction(Cast: DevourPuppet, At: CastTarget.OwnSummon);

        if (BossTiming.Due(context, ShadowErode, ErodeOpensAt, ErodeEvery))
            return new CombatAction(Cast: ShadowErode, At: CastTarget.Self);

        return BossTiming.SummonDue(context, UndeadPuppet, PuppetEvery)
            ? new CombatAction(Summon: UndeadPuppet)
            : null;
    }
}
