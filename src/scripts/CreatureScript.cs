using Ubiquitous.Data;

namespace Ubiquitous.Scripts;

public sealed record GiveUpContext(
    CreatureDefinition? Creature,
    double DistanceToTarget,
    double DistanceFromHome,
    TimeSpan TimeInFight,
    bool LeaderStillFighting,
    TimeSpan? TimeOutsideMeleeRange = null);

public sealed record FleeContext(CreatureDefinition? Creature, double HealthFraction);

public sealed record AssistContext(CreatureDefinition? Creature, double DistanceToFight);

public enum CastTarget
{
    Enemy,

    Self,

    OwnSummon,

    AnyEnemy,

    Ally,

    WoundedAlly,
}

public sealed record CombatTurnContext(
    CreatureDefinition? Creature,
    double HealthFraction,
    TimeSpan TimeInFight,
    bool InMeleeRange,
    IReadOnlyDictionary<uint, TimeSpan> SinceCast,
    IReadOnlyDictionary<uint, int> CastCount,
    int SummonsInReach,
    IReadOnlySet<string> Marks,
    IReadOnlyCollection<uint>? AlliesInReach = null,
    bool Legend = false,
    bool WoundedAllyInReach = false)
{
    public bool HasAllyInReach(uint entryId) => AlliesInReach?.Contains(entryId) == true;
}

public sealed record CombatAction(
    uint? Cast = null,
    CastTarget At = CastTarget.Enemy,
    uint? Summon = null,
    string? Sets = null,
    string? Clears = null,
    string? Yell = null,
    uint? Ends = null,
    TimeSpan? SummonLasts = null,
    uint? AllyEntry = null,
    uint? LeavesPool = null,
    // The creature neither swings nor walks while this (cast-time) spell is being cast.
    bool HoldsStill = false);

public abstract class CreatureScript
{
    public abstract string Name { get; }

    public virtual bool Assists(AssistContext context) => false;

    public virtual bool NoticesEnemies => true;

    public virtual bool FightsBack => true;

    public virtual bool Chases => true;

    public virtual bool DrawsOthersIn => true;

    public virtual string? PullLine => null;

    public virtual string? DeathLine => null;

    public virtual IReadOnlyList<string> Shouts => [];

    // Own choice: how often a creature with idle lines speaks.
    public virtual TimeSpan ShoutEvery => TimeSpan.FromSeconds(45);

    // Auras a fight leaves on the creature that end when it resets rather than dies.
    public virtual IReadOnlyList<uint> ClearedOnReset => [];

    public virtual bool GivesUp(GiveUpContext context)
    {
        var hold = HoldRange(context.Creature);
        bool drawnTooFar = Math.Max(context.DistanceToTarget, context.DistanceFromHome) > hold;
        return drawnTooFar && !context.LeaderStillFighting;
    }

    public virtual bool Flees(FleeContext context) =>
        context.Creature?.Rank is null or CreatureRank.Ordinary
        && context.HealthFraction <= FleeHealthFraction;

    public virtual CombatAction? Act(CombatTurnContext context) => null;

    public virtual IReadOnlyList<uint> DiesInto => [];

    protected static double HoldRange(CreatureDefinition? creature) =>
        creature?.LeashRange is float leash && leash > 0 ? leash : DefaultHoldRange;

    public const double DefaultHoldRange = 40.0;

    public const double FleeHealthFraction = 0.2;
}
