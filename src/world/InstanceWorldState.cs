using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.World;

public sealed record InstancePopulation(
    IReadOnlyList<WorldEntitySnapshot> Creatures,
    IReadOnlyList<WorldEntitySnapshot> StaticObjects);

public sealed class InstanceWorldState
{
    private readonly ushort _mapId;
    private uint _nextEntityId = 1;
    private readonly Dictionary<uint, (EntityId Id, CreatureSpawnDefinition Spawn)> _creatures = new();
    private readonly Dictionary<uint, (EntityId Id, StaticObjectSpawnDefinition Spawn)> _staticObjects = new();
    private readonly Lock _gate = new();
    private readonly HashSet<uint> _deadSpawns = new();
    private readonly Dictionary<uint, DateTimeOffset> _respawnAt = new();
    private readonly List<WorldEntitySnapshot> _summoned = new();
    private bool _populated;

    public InstanceWorldState(ushort mapId) => _mapId = mapId;

    public ushort MapId => _mapId;

    public InstancePopulation EnsurePopulated(
        IEnumerable<CreatureSpawnDefinition> creatureSpawns,
        IEnumerable<StaticObjectSpawnDefinition> staticObjectSpawns)
    {
        if (!_populated)
        {
            _populated = true;
            foreach (var spawn in creatureSpawns.Where(s => s.MapId == _mapId).OrderBy(s => s.Id))
            {
                _creatures[spawn.Id] = (EntityId.From(ObjectType.Creature, _nextEntityId++), spawn);
            }

            foreach (var spawn in staticObjectSpawns.Where(s => s.MapId == _mapId).OrderBy(s => s.Id))
            {
                _staticObjects[spawn.Id] = (EntityId.From(ObjectType.StaticObject, _nextEntityId++), spawn);
            }
        }

        return new InstancePopulation(
            [.. _creatures.Values.Select(ToSnapshot)],
            [.. _staticObjects.Values.Select(ToSnapshot)]);
    }

    public WorldEntitySnapshot? Summon(CreatureSpawnDefinition spawn)
    {
        lock (_gate)
        {
            if (_creatures.ContainsKey(spawn.Id)) return null;
            var id = EntityId.From(ObjectType.Creature, _nextEntityId++);
            _creatures[spawn.Id] = (id, spawn);
            var snapshot = ToSnapshot((id, spawn));
            _summoned.Add(snapshot);
            return snapshot;
        }
    }

    public IReadOnlyList<WorldEntitySnapshot> SummonedSince(ref int cursor)
    {
        lock (_gate)
        {
            if (cursor >= _summoned.Count)
            {
                cursor = _summoned.Count;
                return [];
            }

            var slice = _summoned.Skip(cursor).ToArray();
            cursor = _summoned.Count;
            return slice;
        }
    }

    public void Kill(uint spawnId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_creatures.ContainsKey(spawnId)) return;
            _deadSpawns.Add(spawnId);
        }
    }

    public void ScheduleRespawn(uint spawnId, TimeSpan delay, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_deadSpawns.Contains(spawnId)) return;
            var at = now + delay;
            if (!_respawnAt.TryGetValue(spawnId, out var existing) || at < existing)
                _respawnAt[spawnId] = at;
        }
    }

    public IReadOnlyList<uint> DueRespawns(DateTimeOffset now)
    {
        lock (_gate) return [.. _respawnAt.Where(pair => now >= pair.Value).Select(pair => pair.Key)];
    }

    public void ConfirmRespawned(uint spawnId)
    {
        lock (_gate)
        {
            _deadSpawns.Remove(spawnId);
            _respawnAt.Remove(spawnId);
        }
    }

    public bool TryGetHomeSnapshot(uint spawnId, out WorldEntitySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_creatures.TryGetValue(spawnId, out var entry))
            {
                snapshot = ToSnapshot(entry);
                return true;
            }
            snapshot = null!;
            return false;
        }
    }

    private static WorldEntitySnapshot ToSnapshot((EntityId Id, CreatureSpawnDefinition Spawn) entry) =>
        new(entry.Id, entry.Spawn.CreatureEntryId, new Position(entry.Spawn.X, entry.Spawn.Y, entry.Spawn.Z), entry.Spawn.Yaw, entry.Spawn.Id);

    private static WorldEntitySnapshot ToSnapshot((EntityId Id, StaticObjectSpawnDefinition Spawn) entry) =>
        new(entry.Id, entry.Spawn.EntryId, new Position(entry.Spawn.X, entry.Spawn.Y, entry.Spawn.Z), entry.Spawn.Yaw, entry.Spawn.Id);
}
