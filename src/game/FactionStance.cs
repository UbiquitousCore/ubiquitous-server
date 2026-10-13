using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum Stance
{
    Friendly,

    Wary,

    Hostile,
}

public static class FactionStance
{
    public static int Resolve(int? factionType) => factionType ?? UnitFactionTypes.Hostile;

    public static Stance Between(int self, int target)
    {
        if (self == target) return Stance.Friendly;
        var calm = self == UnitFactionTypes.Neutral || target == UnitFactionTypes.Neutral;
        return calm ? Stance.Wary : Stance.Hostile;
    }

    public static bool AttacksCreatureOnSight(int self, int other) =>
        self == UnitFactionTypes.Hostile && other == UnitFactionTypes.Neutral;
}

