using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.World;

namespace Ubiquitous.Server;

public sealed record ServerContent(
    GameContent Game,
    IReadOnlyList<CreatureSpawnDefinition> CreatureSpawns,
    IReadOnlyList<StaticObjectSpawnDefinition> StaticObjectSpawns,
    WalkableGroundSource? WalkableGround = null)
{
    public static ServerContent Empty { get; } = new(GameContent.Development, [], []);

    public PatrolRouteLibrary Patrols { get; init; } = PatrolRouteLibrary.Fixed(Game.WaypointRoutes);
}
