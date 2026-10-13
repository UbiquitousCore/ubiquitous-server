using Microsoft.Extensions.Logging;
using Ubiquitous.Game;
using Ubiquitous.Modules;

namespace Ubiquitous.Server;

public sealed class ModuleHost(
    string module,
    ModuleSettings settings,
    ServerModules modules,
    PlaySessionFactory sessions,
    PlaySessionHub hub,
    ModuleAccounts accounts,
    int messagesPerSecond,
    ILogger logger) : IModuleHost
{
    public ILogger Logger => logger;

    public ModuleSettings Settings => settings;

    public GameContent Content => sessions.Content.Game;

    public IReadOnlyList<Ubiquitous.Data.CreatureSpawnDefinition> CreatureSpawns => sessions.Content.CreatureSpawns;

    public TimeProvider Time => sessions.World.Time;

    // A floor a little above wins over the bare terrain beneath it (a plaza, a bridge).
    public float? GroundHeight(ushort mapId, float x, float z, float nearY) =>
        sessions.Content.WalkableGround?.ForMap(mapId)?.GroundHeightNear(x, z, nearY, FloorReachUp, Ubiquitous.World.NavigationMesh.MaxFall)
        ?? sessions.Content.Game.Terrain?.HeightAt(mapId, x, z);

    private const float FloorReachUp = 2f;

    public float? WaterSurface(ushort mapId, float x, float z) =>
        sessions.Content.WalkableGround?.WaterSurfaceAt(mapId, x, z);

    public bool HasWalkableGround(ushort mapId) => sessions.Content.WalkableGround?.HasGround(mapId) == true;

    public float WalkableAreaUnder(ushort mapId, Ubiquitous.Common.Position at) =>
        sessions.Content.WalkableGround?.ConnectedAreaUnder(mapId, at) ?? 0f;

    public IReadOnlyList<Ubiquitous.Common.Position>? FindRoute(ushort mapId, Ubiquitous.Common.Position from, Ubiquitous.Common.Position to) =>
        sessions.Content.WalkableGround?.FindRoute(mapId, from, to);

    public bool IsPlayerNear(ushort mapId, Ubiquitous.Common.Position at, float within) =>
        hub.IsPlayerNear(mapId, at, within);

    public Task<ModuleLogin> ReserveLoginAsync(string key, CancellationToken cancellationToken = default) =>
        accounts.ReserveAsync(module, key, cancellationToken);

    public async Task<bool> StartAtLevelAsync(ModuleLogin login, string characterName, int level,
        (ushort MapId, Ubiquitous.Common.Position At)? start = null, CancellationToken cancellationToken = default)
    {
        if (start is { } place && !sessions.Content.Game.Maps.ContainsKey(place.MapId)) return false;
        if (!accounts.Holds(module, login)) return false;
        var characters = sessions.Stores.Characters;
        var character = await characters.FindByNameAsync(characterName, cancellationToken);
        if (character is null || character.AccountId != ModuleAccounts.AccountIdFor(login.UserId)
            || character.Level != CharacterCreation.StartingLevel || character.Xp != 0)
            return false;
        // Full health at the new level: the session works it out from a character with none saved.
        await characters.UpdateAsync(character with
        {
            Level = Math.Clamp(level, CharacterCreation.StartingLevel, LevelingService.MaxLevel),
            Health = null,
            MapId = start?.MapId ?? character.MapId,
            X = start?.At.X ?? character.X,
            Y = start?.At.Y ?? character.Y,
            Z = start?.At.Z ?? character.Z,
        }, cancellationToken);
        return true;
    }

    public IClientConnection Connect() => new LocalConnection(sessions, hub, messagesPerSecond, playedByModule: true);

    public void OnWorldTick(Func<DateTimeOffset, CancellationToken, Task> tick) => modules.AddTick(tick);

    public void OnShutdown(Func<CancellationToken, Task> shutdown) => modules.AddShutdown(shutdown);
}
