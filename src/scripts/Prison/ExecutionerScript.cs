namespace Ubiquitous.Scripts;

public sealed class ExecutionerScript : CreatureScript
{
    public static readonly ExecutionerScript Instance = new();

    private ExecutionerScript() { }

    public override string Name => "boss.executioner";

    private const uint Reaping = 2683;

    private const uint BladeArmor = 2674;

    private const uint Whirlwind = 2675;

    private const string Covered = "covered";

    private static readonly TimeSpan ReapEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RhythmOpensAt = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan RhythmEvery = TimeSpan.FromSeconds(17);

    public override CombatAction? Act(CombatTurnContext context)
    {
        if (BossTiming.SharedDue(context, RhythmOpensAt, RhythmEvery, BladeArmor, Whirlwind))
        {
            var takeArmor = BossTiming.Count(context, BladeArmor) <= BossTiming.Count(context, Whirlwind);
            return takeArmor
                ? new CombatAction(Cast: BladeArmor, At: CastTarget.Self, Sets: Covered)
                : new CombatAction(Cast: Whirlwind, At: CastTarget.Self, Sets: Covered, Yell: "Cyclone Slash!");
        }

        if (context.Marks.Contains(Covered))
        {
            var coveredFor = BossTiming.SinceAny(context, BladeArmor, Whirlwind);
            if (coveredFor is null || coveredFor < BossTiming.CoverHolds) return null;
            return new CombatAction(Clears: Covered);
        }

        if (!context.InMeleeRange) return null;
        return BossTiming.Due(context, Reaping, ReapEvery, ReapEvery)
            ? new CombatAction(Cast: Reaping)
            : null;
    }
}
