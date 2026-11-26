using Ubiquitous.Data;

namespace Ubiquitous.Server;

// What the world holds when no data package is installed, so a fresh clone starts with something to see.
public static class DevelopmentFixtures
{
    public static IReadOnlyList<CreatureSpawnDefinition> CreatureSpawns { get; } =
    [
        new(1001, 13, 9001, 5, 2, 5, 0),
        new(1010, 1, 9010, 24, 3, 4, 3.8f),
        new(1011, 1, 9011, 27, 3, -30, 3.1f),
        new(1012, 1, 9012, 2, 7, 63, 0),
        new(1013, 1, 9013, 5, 7, 64, 0),
        new(3001, 14, 9400, -676, 10, -2092, 0),
        new(3002, 14, 9400, -672, 10, -2090, 0),
        new(3003, 14, 9401, -640, 2, -2300, 0),
    ];

    public static IReadOnlyList<StaticObjectSpawnDefinition> StaticObjectSpawns { get; } =
        [new(2001, 13, 9100, -4, 2, 4, 0)];
}
