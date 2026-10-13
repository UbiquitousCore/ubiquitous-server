using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class SummonRules
{
    // Own choice: every stone stands within a few dozen units of the spot its
    // dungeon puts a player out at, and no two dungeons' spots are this close.
    public const double DungeonReach = 150.0;

    // Two dungeons putting players out at one spot are the normal and legend
    // copies of the same dungeon; the stone asks for the normal one's level.
    private const double SameDoorway = 1.0;

    public static int RequiredLevel(IEnumerable<MapDefinition> maps, ushort mapId, Position stone)
    {
        var doorways = maps
            .Where(map => map.IsDungeon && map.RecallMapId == mapId
                && map.RecallX is not null && map.RecallZ is not null)
            .Select(map => (map.MinLevel, Distance: Math.Sqrt(
                Square(map.RecallX!.Value - stone.X) + Square(map.RecallZ!.Value - stone.Z))))
            .Where(doorway => doorway.Distance <= DungeonReach)
            .ToList();
        if (doorways.Count == 0) return 0;

        var nearest = doorways.Min(doorway => doorway.Distance);
        return doorways.Where(doorway => doorway.Distance <= nearest + SameDoorway).Min(doorway => doorway.MinLevel);
    }

    public static bool CanBeSummoned(int level, int requiredLevel) => level >= requiredLevel;

    private static double Square(double value) => value * value;
}
