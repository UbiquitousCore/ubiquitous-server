namespace Ubiquitous.Scripts;

public sealed class ArielScript : CreatureScript
{
    public static readonly ArielScript Instance = new();

    private ArielScript() { }

    public override string Name => "boss.ariel";

    private const uint DecayedSeed = 2652;

    private const uint Putrefying = 2651;
    private const uint RemovePutrefying = 2796;

    private const uint Detonation = 2657;
    private const uint DetonatesHerself = 2797;

    private const string Ending = "ending";

    private static readonly TimeSpan RootOpensAt = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RootEvery = TimeSpan.FromSeconds(10);

    private const double EndingHealthFraction = 0.10;

    public override CombatAction? Act(CombatTurnContext context)
    {
        if (!context.Marks.Contains(Ending) && context.HealthFraction <= EndingHealthFraction)
            return new CombatAction(Cast: RemovePutrefying, At: CastTarget.Self, Sets: Ending, Ends: Putrefying);

        if (context.Marks.Contains(Ending) && BossTiming.Count(context, Detonation) == 0)
            return new CombatAction(Cast: Detonation, At: CastTarget.Self, HoldsStill: true);

        // The long cast has landed by the time she acts again; the second, instant one ends her.
        if (context.Marks.Contains(Ending) && BossTiming.Count(context, DetonatesHerself) == 0)
            return new CombatAction(Cast: DetonatesHerself, At: CastTarget.Self);

        if (!context.Marks.Contains(Ending) && BossTiming.Count(context, Putrefying) == 0)
            return new CombatAction(Cast: Putrefying, At: CastTarget.Self);

        return BossTiming.Due(context, DecayedSeed, RootOpensAt, RootEvery)
            ? new CombatAction(Cast: DecayedSeed, At: CastTarget.AnyEnemy)
            : null;
    }
}
