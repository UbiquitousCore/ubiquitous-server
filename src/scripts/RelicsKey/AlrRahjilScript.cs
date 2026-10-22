namespace Ubiquitous.Scripts;

public sealed class AlrRahjilScript : CreatureScript
{
    public static readonly AlrRahjilScript Instance = new();

    private AlrRahjilScript() { }

    public override string Name => "boss.alrRahjil";

    private const uint SiphonLife = 2379;
    private const uint SiphonLifeHeal = 2384;
    private const uint ShadowMire = 2382;
    private const uint ShadowMirePool = 2383;
    private const uint Ignite = 2670;
    private const uint WraithBlast = 2381;
    private const uint GrowsLarger = 2385;
    private const uint ShrinksBack = 2386;

    private const double BlastAt = 0.30;

    private static readonly TimeSpan RotationEvery = TimeSpan.FromSeconds(5);

    public override string? PullLine => "You dare disturb me! You will die!";

    public override string? DeathLine => "I will devour your rotten soul when next we meet in hell!";

    public override CombatAction? Act(CombatTurnContext context)
    {
        if (BossTiming.Count(context, SiphonLife) > BossTiming.Count(context, SiphonLifeHeal))
            return new CombatAction(Cast: SiphonLifeHeal, At: CastTarget.Self);

        if (context.HealthFraction <= BlastAt && BossTiming.Count(context, GrowsLarger) == 0)
            return new CombatAction(Cast: GrowsLarger, At: CastTarget.Self);
        if (BossTiming.Count(context, GrowsLarger) > 0 && BossTiming.Count(context, WraithBlast) == 0)
            return new CombatAction(Cast: WraithBlast, At: CastTarget.Self, Yell: "Out, brief life!");
        if (BossTiming.Count(context, WraithBlast) > BossTiming.Count(context, ShrinksBack))
            return new CombatAction(Cast: ShrinksBack, At: CastTarget.Self);

        if (!BossTiming.SharedDue(context, RotationEvery, RotationEvery, SiphonLife, ShadowMire, Ignite)) return null;
        var siphoned = BossTiming.Count(context, SiphonLife);
        var mired = BossTiming.Count(context, ShadowMire);
        var ignited = BossTiming.Count(context, Ignite);
        if (siphoned <= mired && siphoned <= ignited) return new CombatAction(Cast: SiphonLife, At: CastTarget.AnyEnemy);
        if (mired <= ignited)
            return new CombatAction(Cast: ShadowMire, At: CastTarget.AnyEnemy, Yell: "Tremble in terror!", LeavesPool: ShadowMirePool);
        return new CombatAction(Cast: Ignite, At: CastTarget.AnyEnemy);
    }
}
