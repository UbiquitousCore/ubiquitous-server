using Microsoft.Extensions.Logging;
using Ubiquitous.Game;

namespace Ubiquitous.Modules;

public interface IModuleHost
{
    ILogger Logger { get; }

    ModuleSettings Settings { get; }

    GameContent Content { get; }

    // Where each creature is placed, as the spawn tables a client also carries say.
    IReadOnlyList<Ubiquitous.Data.CreatureSpawnDefinition> CreatureSpawns { get; }

    TimeProvider Time { get; }

    float? GroundHeight(ushort mapId, float x, float z, float nearY);

    bool HasWalkableGround(ushort mapId);

    // The water surface over a point, or null where there is no water.
    float? WaterSurface(ushort mapId, float x, float z);

    // How much ground a body standing here could walk to, in square units; 0 off the walkable ground.
    float WalkableAreaUnder(ushort mapId, Ubiquitous.Common.Position at);

    // A walkable route between two points; null when either end is off the walkable ground.
    IReadOnlyList<Ubiquitous.Common.Position>? FindRoute(ushort mapId, Ubiquitous.Common.Position from, Ubiquitous.Common.Position to);

    // Whether a player on a client connection, not one a module plays, stands within reach of a point.
    bool IsPlayerNear(ushort mapId, Ubiquitous.Common.Position at, float within);

    Task<ModuleLogin> ReserveLoginAsync(string key, CancellationToken cancellationToken = default);

    // Raises a character this module's login has just made, not yet played, to `level`, standing at
    // `start` when one is given; false otherwise.
    Task<bool> StartAtLevelAsync(ModuleLogin login, string characterName, int level,
        (ushort MapId, Ubiquitous.Common.Position At)? start = null, CancellationToken cancellationToken = default);

    IClientConnection Connect();

    void OnWorldTick(Func<DateTimeOffset, CancellationToken, Task> tick);

    void OnShutdown(Func<CancellationToken, Task> shutdown);
}
