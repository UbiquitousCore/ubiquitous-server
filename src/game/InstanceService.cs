using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Game;

public enum InstanceEntryCode
{
    Ok,
}

public sealed record InstanceEntryResult(
    InstanceEntryCode Code,
    string? InstanceKey = null,
    InstanceWorldState? World = null,
    SharedMapState? Shared = null,
    DungeonMode Mode = DungeonMode.Normal);

public enum DungeonModeChange
{
    Changed,
    Unchanged,
    Refused,
}

public sealed class InstanceService
{
    private sealed class LiveInstance
    {
        public required string Key { get; init; }
        public required InstanceWorldState World { get; init; }
        public required SharedMapState Shared { get; init; }
        public required DungeonMode Mode { get; init; }
        public HashSet<string> MemberIds { get; } = new(StringComparer.Ordinal);
        public DateTimeOffset? EmptySince { get; set; }
    }

    public static readonly TimeSpan EmptyGrace = TimeSpan.FromMinutes(15);

    public InstanceService(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LiveInstance> _instances = new(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _reached = new(StringComparer.Ordinal);

    private readonly Dictionary<string, DungeonMode> _chosenModes = new(StringComparer.Ordinal);

    public const string RunCompleted = "run.completed";

    public InstanceEntryResult Enter(
        string memberId, ushort mapId, string ownerId, Func<SharedMapState>? createShared = null,
        DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            Sweep(now ?? _time.GetUtcNow());
            var key = $"{mapId}:{ownerId}";
            if (!_instances.TryGetValue(key, out var instance))
            {
                instance = new LiveInstance
                {
                    Key = key,
                    World = new InstanceWorldState(mapId),
                    Shared = createShared?.Invoke() ?? new SharedMapState(mapId, time: _time),
                    Mode = ChosenModeLocked(ownerId),
                };
                _instances[key] = instance;
            }

            instance.MemberIds.Add(memberId);
            instance.EmptySince = null;
            return new InstanceEntryResult(InstanceEntryCode.Ok, key, instance.World, instance.Shared, instance.Mode);
        }
    }

    public void Leave(string memberId, string instanceKey, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            var stamp = now ?? _time.GetUtcNow();
            if (_instances.TryGetValue(instanceKey, out var instance))
            {
                instance.MemberIds.Remove(memberId);
                if (instance.MemberIds.Count == 0) instance.EmptySince = stamp;
            }
            Sweep(stamp);
        }
    }

    public bool ResetOwnedBy(string ownerId)
    {
        lock (_gate) return ResetOwnedByLocked(ownerId);
    }

    public DungeonMode ChosenMode(string ownerId)
    {
        lock (_gate) return ChosenModeLocked(ownerId);
    }

    public DungeonModeChange ChooseMode(string ownerId, DungeonMode mode)
    {
        lock (_gate)
        {
            if (ChosenModeLocked(ownerId) == mode) return DungeonModeChange.Unchanged;
            if (!ResetOwnedByLocked(ownerId)) return DungeonModeChange.Refused;
            _chosenModes[ownerId] = mode;
            return DungeonModeChange.Changed;
        }
    }

    private DungeonMode ChosenModeLocked(string ownerId) =>
        _chosenModes.TryGetValue(ownerId, out var mode) ? mode : DungeonMode.Normal;

    private bool ResetOwnedByLocked(string ownerId)
    {
        var mine = _instances
            .Where(pair => pair.Value.Key.EndsWith($":{ownerId}", StringComparison.Ordinal))
            .ToArray();
        if (mine.Any(pair => pair.Value.MemberIds.Count > 0)) return false;
        foreach (var (key, _) in mine) Forget(key);
        return true;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, instance) in _instances.ToArray())
        {
            if (instance.EmptySince is { } since && now - since >= EmptyGrace) Forget(key);
        }
    }

    private void Forget(string instanceKey)
    {
        _instances.Remove(instanceKey);
        _reached.Remove(instanceKey);
    }

    public bool IsLive(string instanceKey, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            Sweep(now ?? _time.GetUtcNow());
            return _instances.ContainsKey(instanceKey);
        }
    }

    public IReadOnlyList<SharedMapState> LiveWorlds
    {
        get { lock (_gate) return [.. _instances.Values.Select(instance => instance.Shared)]; }
    }

    public IReadOnlyList<string> KeysOwnedBy(string ownerId)
    {
        lock (_gate)
            return [.. _instances.Keys.Where(key => key.EndsWith($":{ownerId}", StringComparison.Ordinal))];
    }

    public bool HasReached(string instanceKey, string milestone)
    {
        lock (_gate)
            return _reached.TryGetValue(instanceKey, out var reached) && reached.Contains(milestone);
    }

    public bool Reach(string instanceKey, string milestone)
    {
        lock (_gate)
        {
            if (!_reached.TryGetValue(instanceKey, out var reached))
            {
                reached = new HashSet<string>(StringComparer.Ordinal);
                _reached[instanceKey] = reached;
            }
            return reached.Add(milestone);
        }
    }

    public IReadOnlyCollection<string> ReachedBy(string instanceKey)
    {
        lock (_gate) return _reached.TryGetValue(instanceKey, out var reached) ? [.. reached] : [];
    }

    public bool IsCompleted(string instanceKey) => HasReached(instanceKey, RunCompleted);

    public void MarkCompleted(string instanceKey) => Reach(instanceKey, RunCompleted);
}
