namespace Ubiquitous.Scripts;

public sealed class KermodeScript : CreatureScript
{
    public static readonly KermodeScript Instance = new();

    private KermodeScript() { }

    public override string Name => "boss.kermode";

    private const uint DeafeningRoar = 2370;
    private const uint SoulGrowl = 2371;
    private const uint SpiritBearFrenzy = 2372;

    private const double FrenzyAt = 0.30;

    private static readonly TimeSpan BetweenShouts = TimeSpan.FromSeconds(8);

    public override CombatAction? Act(CombatTurnContext context)
    {
        if (context.HealthFraction <= FrenzyAt && BossTiming.Count(context, SpiritBearFrenzy) == 0)
            return new CombatAction(Cast: SpiritBearFrenzy, At: CastTarget.Self);

        if (!BossTiming.SharedDue(context, BetweenShouts, BetweenShouts, SoulGrowl, DeafeningRoar)) return null;

        // Normal opens on the growl; Legend opens on the roar. Either way the two take turns.
        var (first, second) = context.Legend ? (DeafeningRoar, SoulGrowl) : (SoulGrowl, DeafeningRoar);
        var next = BossTiming.Count(context, first) <= BossTiming.Count(context, second) ? first : second;
        return new CombatAction(Cast: next, At: CastTarget.Self);
    }
}
