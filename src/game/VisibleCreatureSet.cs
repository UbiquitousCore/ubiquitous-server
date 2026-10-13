using Ubiquitous.Common;
using Ubiquitous.World;

namespace Ubiquitous.Game;

// What one session can see of a map's creatures: entry, spawn and targeting
// position per wire id, kept together so a sighting or a forgetting cannot
// leave one of them behind.
public sealed class VisibleCreatureSet
{
    private readonly Dictionary<string, uint> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _spawnIds = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _wireIdsBySpawnId = new();
    private readonly Dictionary<string, TargetCandidate> _targets = new(StringComparer.Ordinal);
    private string[]? _ids;

    public IReadOnlyDictionary<string, uint> Entries => _entries;
    public IReadOnlyDictionary<string, uint> SpawnIds => _spawnIds;
    public IReadOnlyDictionary<uint, string> WireIdsBySpawnId => _wireIdsBySpawnId;
    public IReadOnlyCollection<TargetCandidate> Targets => _targets.Values;

    // A tick walks the set several times and may forget creatures on the way,
    // so each walk needs a copy; one copy serves until the set changes.
    public IReadOnlyList<string> Ids => _ids ??= [.. _entries.Keys];

    public bool Contains(string wireId) => _entries.ContainsKey(wireId);

    public void Add(WorldEntitySnapshot entity)
    {
        var wireId = entity.Id.ToString();
        if (!_entries.ContainsKey(wireId)) _ids = null;
        _entries[wireId] = entity.Entry;
        _spawnIds[wireId] = entity.SpawnId;
        _wireIdsBySpawnId[entity.SpawnId] = wireId;
        _targets[wireId] = new TargetCandidate(wireId, entity.Position.X, entity.Position.Y, entity.Position.Z);
    }

    public void Move(string wireId, Position position)
    {
        if (_targets.ContainsKey(wireId))
            _targets[wireId] = new TargetCandidate(wireId, position.X, position.Y, position.Z);
    }

    public void Remove(string wireId)
    {
        if (_entries.Remove(wireId)) _ids = null;
        if (_spawnIds.Remove(wireId, out var spawnId)
            && _wireIdsBySpawnId.TryGetValue(spawnId, out var mapped)
            && string.Equals(mapped, wireId, StringComparison.Ordinal))
            _wireIdsBySpawnId.Remove(spawnId);
        _targets.Remove(wireId);
    }

    public void Clear()
    {
        _entries.Clear();
        _spawnIds.Clear();
        _wireIdsBySpawnId.Clear();
        _targets.Clear();
        _ids = null;
    }
}
