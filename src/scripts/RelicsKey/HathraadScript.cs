namespace Ubiquitous.Scripts;

public sealed class HathraadScript : CreatureScript
{
    public static readonly HathraadScript Instance = new();

    private HathraadScript() { }

    public override string Name => "boss.hathraad";

    private const uint WaterBall = 2373;
    private const uint MultiWaterBall = 2375;

    public const uint MirrorImage = 2626;

    private static readonly double[] SplitsAt = [0.75, 0.50, 0.25];

    private const int ImagesPerSplit = 2;

    private static readonly TimeSpan TempestEvery = TimeSpan.FromSeconds(10);

    // Own choice: the guide says he throws water balls "constantly"; each ball has a 3 s cast.
    private static readonly TimeSpan BallEvery = TimeSpan.FromSeconds(4);

    public override string? PullLine => "Heretics must die!";

    public override string? DeathLine => "No....";

    public override CombatAction? Act(CombatTurnContext context)
    {
        var images = BossTiming.Count(context, BossTiming.SummonKey(MirrorImage));
        var owed = SplitsAt.Count(step => context.HealthFraction <= step) * ImagesPerSplit;
        if (images < owed)
            return new CombatAction(Summon: MirrorImage, Yell: images % ImagesPerSplit == 0 ? "Dividedness!" : null);

        if (BossTiming.Due(context, MultiWaterBall, TempestEvery, TempestEvery))
            return new CombatAction(Cast: MultiWaterBall, At: CastTarget.Self, Yell: "Tempest!");

        return BossTiming.Ready(context, WaterBall, BallEvery) ? new CombatAction(Cast: WaterBall) : null;
    }
}
