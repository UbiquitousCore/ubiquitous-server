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
        var content = sessions.Content.Game;
        var startingLevel = content.LoadoutFor(character?.Race ?? 0, character?.Class ?? 0)?.Level ?? CharacterCreation.StartingLevel;
        if (character is null || character.AccountId != ModuleAccounts.AccountIdFor(login.UserId)
            || character.Level != startingLevel || character.Xp != 0 || character.LearnedSpells is { Count: > 0 })
            return false;
        // A class that starts above `level` (a Flame Knight) stays at its own start.
        var raised = Math.Clamp(level, startingLevel, content.LevelCap);
        // Full health at the new level: the session works it out from a character with none saved.
        await characters.UpdateAsync(character with
        {
            Level = raised,
            LearnedSpells = LessonsUpTo(content, character.Race, character.Class, raised),
            Health = null,
            MapId = start?.MapId ?? character.MapId,
            X = start?.At.X ?? character.X,
            Y = start?.At.Y ?? character.Y,
            Z = start?.At.Z ?? character.Z,
        }, cancellationToken);
        return true;
    }

    // A later build's character raised here knows the ranks its class's own lessons teach by that level,
    // as one that had played up to it could; a 1.1.4 character learns them in play.
    private static IReadOnlyList<uint>? LessonsUpTo(GameContent content, int race, int playerClass, int level)
    {
        if (content.Spellbook is null) return null;
        var start = content.LoadoutFor(race, playerClass)?.Spells ?? [];
        var learned = ClassSpellbook.WholeBookAt(content, playerClass, level).Where(id => !start.Contains(id)).ToArray();
        return learned.Length == 0 ? null : learned;
    }

    public IClientConnection Connect() => new LocalConnection(sessions, hub, messagesPerSecond, playedByModule: true);

    public void OnWorldTick(Func<DateTimeOffset, CancellationToken, Task> tick) => modules.AddTick(tick);

    public void OnShutdown(Func<CancellationToken, Task> shutdown) => modules.AddShutdown(shutdown);
}
