using Ubiquitous.Data;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Modules.Playerbots;

public enum BotRole
{
    Tank,
    Healer,
    Damage,
}

// How a bot plays alongside other players: its part in a group, and leaving a
// creature to whoever got to it first.
public sealed partial class BotBrain
{
    private const float HealBelow = 0.6f;
    private const float ClaimReach = 12f;
    private readonly uint _salt = (uint)random.Next();

    public static BotRole RoleOf(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => BotRole.Tank,
        PlayerClassIds.Monk => BotRole.Healer,
        _ => BotRole.Damage,
    };

    private BotDecision? HealGroup(BotView view, DateTimeOffset now)
    {
        if (!view.InParty || RoleOf(view.Class) != BotRole.Healer) return null;
        var hurt = view.PartyMemberIds
            .Where(id => id != view.SelfId)
            .Select(id => view.Units.GetValueOrDefault(id))
            .OfType<SeenUnit>()
            .Where(mate => !mate.Dead && mate.MaxHealth > 0 && (float)mate.Health / mate.MaxHealth < HealBelow)
            .OrderBy(mate => (float)mate.Health / mate.MaxHealth)
            .FirstOrDefault();
        if (hurt is null || UsableSpell(view, now, knowledge.HealsOthers) is not { } heal) return null;
        var reach = Math.Max(heal.MaxRange, MeleeReach);
        if (Flat(view.Position, hurt.Position) > reach * 0.9f) return new WalkTo(hurt.Position, reach * 0.6f);
        return Cast(heal, hurt.Id, now);
    }

    // A tank takes on whatever is hitting someone else in its group first.
    private SeenUnit? Protect(BotView view)
    {
        if (!view.InParty || RoleOf(view.Class) != BotRole.Tank) return null;
        return view.Units.Values
            .Where(unit => unit is { Kind: UnitKind.Creature, Dead: false } && unit.Targeting is { } victim
                && victim != view.SelfId && view.PartyMemberIds.Contains(victim))
            .OrderBy(unit => Flat(view.Position, unit.Position))
            .FirstOrDefault();
    }

    // A creature someone outside the group is fighting, or is nearer to, is theirs;
    // the bot joins in on a few anyway (doubleUpPercent), as players sometimes do.
    private bool LeftToOthers(BotView view, SeenUnit creature)
    {
        if (DoublesUpOn(creature.Id)) return false;
        bool Outsider(string id) => id != view.SelfId && !view.PartyMemberIds.Contains(id);
        if (creature.Targeting is { } victim && victim.StartsWith("player:", StringComparison.Ordinal) && Outsider(victim))
            return true;

        var mine = Flat(view.Position, creature.Position);
        return view.Units.Values.Any(player => player is { Kind: UnitKind.Player, Dead: false } && Outsider(player.Id)
            && (player.Targeting == creature.Id
                || Flat(player.Position, creature.Position) < Math.Min(mine, ClaimReach)));
    }

    private bool DoublesUpOn(string creatureId)
    {
        var hash = 2166136261u ^ _salt;
        foreach (var c in creatureId) hash = (hash ^ c) * 16777619u;
        return hash % 100 < settings.DoubleUpPercent;
    }
}
