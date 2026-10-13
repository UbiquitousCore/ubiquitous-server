namespace Ubiquitous.Scripts;

public sealed class DefaultCreatureScript : CreatureScript
{
    public static readonly DefaultCreatureScript Instance = new();

    public override string Name => "creature.default";
}

public sealed class GuardScript : CreatureScript
{
    public static readonly GuardScript Instance = new();

    public override string Name => "creature.guard";

    public override bool Assists(AssistContext context) => context.DistanceToFight <= ReactionRange;

    public const double ReactionRange = 15.0;
}

public sealed class TrainingDummyScript : CreatureScript
{
    public static readonly TrainingDummyScript Instance = new();

    public override string Name => "creature.trainingDummy";

    public override bool NoticesEnemies => false;

    public override bool FightsBack => false;

    public override bool Chases => false;

    public override bool DrawsOthersIn => false;

    public override bool GivesUp(GiveUpContext context) =>
        context.TimeOutsideMeleeRange is { } quiet && quiet >= CombatExitDelay;

    public static readonly TimeSpan CombatExitDelay = TimeSpan.FromSeconds(8);
}

public sealed class DungeonInmateScript : CreatureScript
{
    public static readonly DungeonInmateScript Instance = new();

    public override string Name => "creature.dungeonInmate";

    public override bool GivesUp(GiveUpContext context) =>
        context.DistanceToTarget > HoldRange(context.Creature)
        && context.TimeOutsideMeleeRange >= OutrunTime
        && !context.LeaderStillFighting;

    public static readonly TimeSpan OutrunTime = TimeSpan.FromSeconds(5);
}
