using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Game;

public sealed record MapMovement(
    long Sequence,
    DateTimeOffset At,
    string WireId,
    Position Position,
    Position? Target,
    bool Moving,
    double Speed);

public sealed class SharedMapState : IHostilityLog
{
    private readonly List<MapMovement> _movements = [];
    private long _movementSequence;

    private static readonly TimeSpan MovementHistory = TimeSpan.FromSeconds(30);

    public long MovementCursor
    {
        get { lock (_gate) return _movementSequence; }
    }

    public IReadOnlyList<MapMovement> MovementsSince(long cursor, Func<string, bool> wanted, out long latest)
    {
        lock (_gate)
        {
            latest = Math.Max(cursor, _movementSequence);
            var result = new List<MapMovement>();
            for (var i = FirstMovementAfter(cursor); i < _movements.Count; i++)
                if (wanted(_movements[i].WireId)) result.Add(_movements[i]);
            return result;
        }
    }

    private int FirstMovementAfter(long cursor)
    {
        int low = 0, high = _movements.Count;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (_movements[mid].Sequence > cursor) high = mid;
            else low = mid + 1;
        }
        return low;
    }

    public void TickWorld(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_movements.Count > 0)
            {
                var cutoff = now - MovementHistory;
                _movements.RemoveAll(movement => movement.At < cutoff);
            }

            foreach (var (wireId, _) in _returning.ToArray())
            {
                if (!_positions.TryGetValue(wireId, out var current)) continue;
                var update = AdvanceReturn(wireId, current, now);
                if (update is null) continue;
                _positions[wireId] = update.Position;
                if (!update.IsTransition) continue;
                _movements.Add(new MapMovement(
                    ++_movementSequence, now, wireId, update.Position, update.Target,
                    update.Moving, CreatureChaseService.Speed));
            }
        }
    }

    private readonly object _gate = new();
    private readonly ushort _mapId;
    private readonly SpawnsOnMap<CreatureSpawnDefinition> _creatureSpawnsOnMap = new();
    private readonly SpawnsOnMap<StaticObjectSpawnDefinition> _staticSpawnsOnMap = new();
    private readonly Func<double> _rollUnit;
    private uint _nextEntityId = 1;
    private readonly Dictionary<uint, string> _creatureWireIds = new();
    private readonly Dictionary<uint, string> _staticWireIds = new();
    private readonly Dictionary<string, uint> _miningLocationOf = new(StringComparer.Ordinal);
    private MiningPool? _mining;
    private readonly Dictionary<string, Position> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _creatureSpawnOf = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _deadSpawns = new();
    private readonly Dictionary<uint, DateTimeOffset> _respawnAt = new();
    private readonly Dictionary<uint, DateTimeOffset> _corpseUntil = new();
    private readonly Dictionary<uint, DateTimeOffset> _killedAt = new();
    private readonly Dictionary<string, int> _engaged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Position> _routeDestinations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, double>> _hostility = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mobVictims = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _fighters = new(StringComparer.Ordinal);
    private static readonly TimeSpan FighterExpiry = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _time;
    private readonly CreatureWanderService _wander;
    private readonly CreatureWaypointRouteService _route;
    private readonly Dictionary<string, WaypointRouteDefinition> _routeOf = new(StringComparer.Ordinal);
    private readonly CreatureFollowService _follow;
    private readonly Func<float, float, float?, GroundAnswer?> _groundHeight;
    private readonly Func<Position, Position, bool>? _canWalkBetween;
    private readonly Dictionary<uint, WaypointRouteDefinition> _patrols = new();
    private readonly Dictionary<string, ReturnLeg> _returning = new(StringComparer.Ordinal);

    public SharedMapState(
        ushort mapId,
        Func<double>? rollUnit = null,
        Func<float, float, float?, GroundAnswer?>? groundHeight = null,
        Func<Position, Position, bool>? canWalkBetween = null,
        TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _mapId = mapId;
        _rollUnit = rollUnit ?? Random.Shared.NextDouble;
        _groundHeight = groundHeight ?? ((_, _, _) => null);
        _canWalkBetween = canWalkBetween;
        _wander = new CreatureWanderService(_rollUnit, _groundHeight, canWalkBetween);
        _route = new CreatureWaypointRouteService(_groundHeight);
        _follow = new CreatureFollowService(_groundHeight);
    }

    public ushort MapId => _mapId;

    public AuraBoard Auras { get; } = new();

    private string MintCreature(uint spawnId)
    {
        if (!_creatureWireIds.TryGetValue(spawnId, out var wireId))
        {
            wireId = EntityId.From(ObjectType.Creature, _nextEntityId++).ToString();
            _creatureWireIds[spawnId] = wireId;
        }
        return wireId;
    }

    private string MintStatic(uint spawnId)
    {
        if (!_staticWireIds.TryGetValue(spawnId, out var wireId))
        {
            wireId = EntityId.From(ObjectType.StaticObject, _nextEntityId++).ToString();
            _staticWireIds[spawnId] = wireId;
        }
        return wireId;
    }

    private static bool InRange(Position point, Position player, float range) =>
        new Position(point.X, point.Y, point.Z).DistanceSquaredTo(player) <= range * range;

    public IReadOnlyList<WorldEntitySnapshot> VisibleCreatures(
        Position player, float range, IEnumerable<CreatureSpawnDefinition> spawns)
    {
        lock (_gate)
        {
            var result = new List<WorldEntitySnapshot>();
            foreach (var spawn in _creatureSpawnsOnMap.For(spawns, _mapId, s => s.MapId, s => s.Id))
            {
                if (_deadSpawns.Contains(spawn.Id)) continue;
                var wireId = MintCreature(spawn.Id);
                if (!_positions.TryGetValue(wireId, out var position))
                {
                    position = new Position(spawn.X, spawn.Y, spawn.Z);
                    _positions[wireId] = position;
                    _creatureSpawnOf[wireId] = spawn.Id;
                }
                if (!InRange(position, player, range)) continue;
                result.Add(new WorldEntitySnapshot(
                    EntityId.Parse(wireId), spawn.CreatureEntryId, position, spawn.Yaw, spawn.Id));
            }
            return result;
        }
    }

    public IReadOnlyList<WorldEntitySnapshot> VisibleStaticObjects(
        Position player, float range, IEnumerable<StaticObjectSpawnDefinition> spawns)
    {
        lock (_gate)
        {
            var onMap = _staticSpawnsOnMap.For(spawns, _mapId, s => s.MapId, s => s.Id);
            _mining ??= new MiningPool(
                onMap.Where(s => s.EntryId == MiningRules.LocationEntry).Select(s => s.Id),
                MiningOres.ForMap(_mapId), _rollUnit);
            var result = new List<WorldEntitySnapshot>();
            foreach (var spawn in onMap)
            {
                var entry = spawn.EntryId;
                if (entry == MiningRules.LocationEntry)
                {
                    if (!_mining.TryGetActive(spawn.Id, out var ore)) continue;
                    entry = ore.NodeEntry;
                }

                var wireId = MintStatic(spawn.Id);
                if (spawn.EntryId == MiningRules.LocationEntry) _miningLocationOf[wireId] = spawn.Id;
                if (!_positions.TryGetValue(wireId, out var position))
                {
                    position = new Position(spawn.X, spawn.Y, spawn.Z);
                    _positions[wireId] = position;
                }
                if (!InRange(position, player, range)) continue;
                result.Add(new WorldEntitySnapshot(
                    EntityId.Parse(wireId), entry, position, spawn.Yaw, spawn.Id));
            }
            return result;
        }
    }

    public long MiningVersion
    {
        get { lock (_gate) return _mining?.Version ?? 0; }
    }

    public MiningOre? ActiveOreAt(string wireId)
    {
        lock (_gate)
            return _mining is not null && _miningLocationOf.TryGetValue(wireId, out var location)
                && _mining.TryGetActive(location, out var ore) ? ore : null;
    }

    public MiningOre? Mine(string wireId, DateTimeOffset now)
    {
        lock (_gate)
            return _mining is not null && _miningLocationOf.TryGetValue(wireId, out var location)
                ? _mining.Mine(location, now) : null;
    }

    public void Adopt(string wireId, uint spawnId, Position position)
    {
        lock (_gate)
        {
            _creatureSpawnOf[wireId] = spawnId;
            if (!_positions.ContainsKey(wireId)) _positions[wireId] = position;
        }
    }

    public string MintCreatureId(uint spawnId)
    {
        lock (_gate) return MintCreature(spawnId);
    }

    // Drop everything a runtime spawn added, so a despawned bot leaves no residue for the next session.
    public void ForgetSpawn(uint spawnId)
    {
        lock (_gate)
        {
            if (!_creatureWireIds.Remove(spawnId, out var wireId)) return;
            _positions.Remove(wireId);
            _creatureSpawnOf.Remove(wireId);
            _engaged.Remove(wireId);
            _routeDestinations.Remove(wireId);
            _returning.Remove(wireId);
            _mobVictims.Remove(wireId);
            _fighters.Remove(wireId);
            _hostility.Remove(wireId);
            _awakeAt.Remove(wireId);
            _deadSpawns.Remove(spawnId);
            _corpseUntil.Remove(spawnId);
            _respawnAt.Remove(spawnId);
            _killedAt.Remove(spawnId);
            _wander.Clear(wireId);
            _follow.Clear(wireId);
            _route.Clear(wireId);
            foreach (var entries in _hostility.Values) entries.Remove(wireId);
            foreach (var fighters in _fighters.Values) fighters.Remove(wireId);
        }
    }

    public bool TryGetPosition(string wireId, out Position position)
    {
        lock (_gate) return _positions.TryGetValue(wireId, out position);
    }

    public void SetPosition(string wireId, Position position)
    {
        lock (_gate) _positions[wireId] = position;
    }

    public bool IsAlive(string wireId)
    {
        lock (_gate)
            return _creatureSpawnOf.TryGetValue(wireId, out var spawnId) && !_deadSpawns.Contains(spawnId);
    }

    public void AddHostility(string mobId, string attackerId, double amount)
    {
        lock (_gate)
        {
            if (!_hostility.TryGetValue(mobId, out var entries))
            {
                entries = new Dictionary<string, double>(StringComparer.Ordinal);
                _hostility[mobId] = entries;
            }
            entries[attackerId] = entries.TryGetValue(attackerId, out var held) ? held + amount : amount;
        }
    }

    public IReadOnlyDictionary<string, double> HostilityFor(string mobId)
    {
        lock (_gate)
            return _hostility.TryGetValue(mobId, out var entries)
                ? new Dictionary<string, double>(entries, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);
    }

    public void ForgetAttacker(string mobId, string attackerId)
    {
        lock (_gate)
        {
            if (_hostility.TryGetValue(mobId, out var entries))
            {
                entries.Remove(attackerId);
                if (entries.Count == 0) _hostility.Remove(mobId);
            }
            if (_mobVictims.TryGetValue(mobId, out var victim)
                && string.Equals(victim, attackerId, StringComparison.Ordinal))
                _mobVictims.Remove(mobId);
            if (_fighters.TryGetValue(mobId, out var set))
            {
                set.Remove(attackerId);
                if (set.Count == 0) _fighters.Remove(mobId);
            }
        }
    }

    public void ClearHostility(string mobId)
    {
        lock (_gate)
        {
            _hostility.Remove(mobId);
            _mobVictims.Remove(mobId);
        }
    }

    public string? MobVictim(string mobId)
    {
        lock (_gate) return _mobVictims.TryGetValue(mobId, out var victim) ? victim : null;
    }

    public void SetMobVictim(string mobId, string attackerId)
    {
        lock (_gate) _mobVictims[mobId] = attackerId;
    }

    public void ClearMobVictim(string mobId)
    {
        lock (_gate) _mobVictims.Remove(mobId);
    }

    public IReadOnlyCollection<string> Fighters(string mobId)
    {
        lock (_gate)
        {
            if (!_fighters.TryGetValue(mobId, out var set)) return Array.Empty<string>();
            var cutoff = _time.GetUtcNow() - FighterExpiry;
            return set.Where(pair => pair.Value >= cutoff).Select(pair => pair.Key).ToArray();
        }
    }

    public void AddFighter(string mobId, string attackerId)
    {
        lock (_gate)
        {
            if (!_fighters.TryGetValue(mobId, out var set))
            {
                set = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                _fighters[mobId] = set;
            }
            set[attackerId] = _time.GetUtcNow();
        }
    }

    public void RemoveFighter(string mobId, string attackerId)
    {
        lock (_gate)
        {
            if (_fighters.TryGetValue(mobId, out var set))
            {
                set.Remove(attackerId);
                if (set.Count == 0) _fighters.Remove(mobId);
            }
        }
    }

    public void ClearFighters(string mobId)
    {
        lock (_gate) _fighters.Remove(mobId);
    }

    void IHostilityLog.Add(string mobId, string attackerId, double amount) => AddHostility(mobId, attackerId, amount);

    IReadOnlyDictionary<string, double> IHostilityLog.For(string mobId) => HostilityFor(mobId);

    void IHostilityLog.Clear(string mobId) => ClearHostility(mobId);

    string? IHostilityLog.Victim(string mobId) => MobVictim(mobId);

    void IHostilityLog.SetVictim(string mobId, string attackerId) => SetMobVictim(mobId, attackerId);

    void IHostilityLog.ClearVictim(string mobId) => ClearMobVictim(mobId);

    public void Kill(uint spawnId, DateTimeOffset now)
    {
        lock (_gate)
        {
            _deadSpawns.Add(spawnId);
            _killedAt[spawnId] = now;
            if (_creatureWireIds.TryGetValue(spawnId, out var wireId))
            {
                _engaged.Remove(wireId);
                _returning.Remove(wireId);
                _hostility.Remove(wireId);
                _mobVictims.Remove(wireId);
                EndHuntsOf(wireId);
            }
        }
    }

    private readonly Dictionary<string, string> _hunts = new(StringComparer.Ordinal);

    public bool TryClaimHunt(string hunterId, string preyId)
    {
        lock (_gate)
        {
            if (IsInHuntCore(hunterId) || IsInHuntCore(preyId)) return false;
            _hunts[hunterId] = preyId;
            return true;
        }
    }

    public void ReleaseHunt(string hunterId)
    {
        lock (_gate) _hunts.Remove(hunterId);
    }

    public bool IsInHunt(string wireId)
    {
        lock (_gate) return IsInHuntCore(wireId);
    }

    private bool IsInHuntCore(string wireId) =>
        _hunts.ContainsKey(wireId) || _hunts.ContainsValue(wireId);

    private void EndHuntsOf(string wireId)
    {
        _hunts.Remove(wireId);
        foreach (var (hunterId, preyId) in _hunts.ToArray())
            if (string.Equals(preyId, wireId, StringComparison.Ordinal)) _hunts.Remove(hunterId);
    }

    public bool TryGetWireId(uint spawnId, out string wireId)
    {
        lock (_gate) return _creatureWireIds.TryGetValue(spawnId, out wireId!);
    }

    public bool TryGetCorpseUntil(uint spawnId, out DateTimeOffset until)
    {
        lock (_gate) return _corpseUntil.TryGetValue(spawnId, out until);
    }

    public void HoldCorpse(uint spawnId, DateTimeOffset until)
    {
        lock (_gate)
            _corpseUntil[spawnId] = CorpseLifetime.Sooner(
                _corpseUntil.TryGetValue(spawnId, out var held) ? held : null, until);
    }

    public void ScheduleRespawn(uint spawnId, TimeSpan respawnDelay, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_deadSpawns.Contains(spawnId)) return;
            _corpseUntil[spawnId] = now;
            var at = now + respawnDelay;
            if (!_respawnAt.TryGetValue(spawnId, out var existing) || at < existing)
                _respawnAt[spawnId] = at;
        }
    }

    public void ScheduleRespawnIfAbsent(uint spawnId, TimeSpan respawnDelay, TimeSpan corpsePad, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_deadSpawns.Contains(spawnId) || _respawnAt.ContainsKey(spawnId)) return;
            var slainAt = _killedAt.TryGetValue(spawnId, out var slain) ? slain : now;
            _corpseUntil[spawnId] = slainAt + corpsePad;
            _respawnAt[spawnId] = slainAt + corpsePad + respawnDelay;
        }
    }

    public IReadOnlyList<uint> CollectDueRespawns(DateTimeOffset now)
    {
        lock (_gate)
        {
            var due = _respawnAt.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToArray();
            foreach (var spawnId in due)
            {
                _deadSpawns.Remove(spawnId);
                _respawnAt.Remove(spawnId);
                _corpseUntil.Remove(spawnId);
                _killedAt.Remove(spawnId);
                if (_creatureWireIds.Remove(spawnId, out var wireId))
                {
                    _creatureSpawnOf.Remove(wireId);
                    _positions.Remove(wireId);
                }
            }
            return due;
        }
    }

    // Where a creature stands at this moment: partway along the walk it is on, else where it was last put.
    public Position? PositionNow(string wireId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var walking = _wander.PositionAt(wireId, now)
                ?? (_routeOf.TryGetValue(wireId, out var route) ? _route.PositionAt(wireId, route, now) : null)
                ?? ReturningAt(wireId, now);
            return walking ?? (_positions.TryGetValue(wireId, out var at) ? at : null);
        }
    }

    // A creature pulled into a fight stops where it is now, not where its last walking update left it.
    public Position? Settle(string wireId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var walking = _wander.PositionAt(wireId, now)
                ?? (_routeOf.TryGetValue(wireId, out var route) ? _route.PositionAt(wireId, route, now) : null);
            if (walking is not { } settled) return null;
            _wander.Clear(wireId);
            _route.Halt(wireId, now);
            _routeDestinations.Remove(wireId);
            _positions[wireId] = settled;
            return settled;
        }
    }

    private Position? ReturningAt(string wireId, DateTimeOffset now)
    {
        if (!_returning.TryGetValue(wireId, out var leg) || leg.Destination is not { } destination
            || leg.StartedAt is not { } started || leg.ArrivesAt is not { } arrives)
            return null;
        var totalSeconds = (arrives - started).TotalSeconds;
        var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - started).TotalSeconds / totalSeconds, 0.0, 1.0);
        return StepToward(leg.Origin, destination, t);
    }

    public void MarkEngaged(string wireId)
    {
        lock (_gate) _engaged[wireId] = _engaged.TryGetValue(wireId, out var count) ? count + 1 : 1;
    }

    public void MarkDisengaged(string wireId)
    {
        lock (_gate)
        {
            if (!_engaged.TryGetValue(wireId, out var count)) return;
            if (count <= 1) _engaged.Remove(wireId);
            else _engaged[wireId] = count - 1;
        }
    }

    public bool IsEngaged(string wireId)
    {
        lock (_gate) return _engaged.ContainsKey(wireId);
    }

    public void Revive(uint spawnId)
    {
        lock (_gate)
        {
            _deadSpawns.Remove(spawnId);
            _corpseUntil.Remove(spawnId);
            _respawnAt.Remove(spawnId);
            _killedAt.Remove(spawnId);
        }
    }

    private readonly Dictionary<string, DateTimeOffset> _awakeAt = new(StringComparer.Ordinal);

    public bool IsSimulating(string wireId, Position home, Position player, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_engaged.ContainsKey(wireId) || (_fighters.TryGetValue(wireId, out var fighters) && fighters.Count > 0))
            {
                _awakeAt[wireId] = now;
                return true;
            }
            if (TileGrid.IsActive(home, player))
            {
                _awakeAt[wireId] = now;
                return true;
            }
            return _awakeAt.TryGetValue(wireId, out var last) && now - last < TileGrid.SleepGrace;
        }
    }

    public void MarkReturning(string wireId, Position home)
    {
        lock (_gate)
        {
            if (_returning.ContainsKey(wireId)) return;
            _returning[wireId] = new ReturnLeg(home);
        }
    }


    public void ClearReturning(string wireId)
    {
        lock (_gate) _returning.Remove(wireId);
    }

    public bool IsReturning(string wireId)
    {
        lock (_gate) return _returning.ContainsKey(wireId);
    }

    private WanderUpdate? AdvanceReturn(string wireId, Position current, DateTimeOffset now)
    {
        {
            if (!_returning.TryGetValue(wireId, out var leg)) return null;
            if (GroundDistance(current, leg.Home) < 1.0)
            {
                _returning.Remove(wireId);
                return null;
            }
            if (leg.ArrivesAt is null || leg.Destination is null)
            {
                var distance = GroundDistance(current, leg.Home);
                var travelSeconds = Math.Max(1.0, distance / MovementRules.PlayerForwardSpeed);
                leg.Origin = current;
                leg.Destination = leg.Home;
                leg.StartedAt = now;
                leg.ArrivesAt = now + TimeSpan.FromSeconds(travelSeconds);
                return new WanderUpdate(current, Moving: true, Target: leg.Home, IsTransition: true);
            }
            var destination = leg.Destination;
            var arrivesAt = leg.ArrivesAt;
            if (destination is null || arrivesAt is null) return null;
            if (now >= arrivesAt.Value)
            {
                _returning.Remove(wireId);
                return new WanderUpdate(destination.Value, Moving: false, Target: null, IsTransition: true);
            }
            var totalSeconds = (arrivesAt.Value - leg.StartedAt!.Value).TotalSeconds;
            var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - leg.StartedAt.Value).TotalSeconds / totalSeconds, 0.0, 1.0);
            var stepped = StepToward(leg.Origin, destination.Value, t);
            return new WanderUpdate(stepped, Moving: true, Target: destination.Value, IsTransition: false);
        }
    }

    private Position StepToward(Position current, Position destination, double t)
    {
        var x = (float)(current.X + (destination.X - current.X) * t);
        var z = (float)(current.Z + (destination.Z - current.Z) * t);
        return new Position(x, _groundHeight(x, z, current.Y)?.Y ?? current.Y, z);
    }

    private static double GroundDistance(Position from, Position to)
    {
        double dx = to.X - from.X;
        double dz = to.Z - from.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private sealed class ReturnLeg(Position home)
    {
        public Position Home = home;
        public Position Origin;
        public Position? Destination;
        public DateTimeOffset? StartedAt;
        public DateTimeOffset? ArrivesAt;
    }

    public WanderUpdate? AdvanceWander(
        string wireId, Position current, Position home, double wanderRadius, DateTimeOffset now)
    {
        lock (_gate) return _wander.Advance(wireId, current, home, wanderRadius, now);
    }

    public WaypointRouteDefinition PatrolFor(CreatureSpawnDefinition spawn)
    {
        lock (_gate)
        {
            if (!_patrols.TryGetValue(spawn.Id, out var route))
            {
                route = PatrolRoutes.Generate(spawn, _canWalkBetween, _groundHeight);
                _patrols[spawn.Id] = route;
            }

            return route;
        }
    }

    public RouteUpdate? AdvanceRoute(
        string wireId, Position current, WaypointRouteDefinition route, DateTimeOffset now)
    {
        lock (_gate)
        {
            _routeOf[wireId] = route;
            var update = _route.Advance(wireId, current, route, now);
            if (update is null) return null;
            if (update.Target is { } destination) _routeDestinations[wireId] = destination;
            else _routeDestinations.Remove(wireId);
            return update;
        }
    }

    public bool TryGetRouteDestination(string wireId, out Position destination)
    {
        lock (_gate) return _routeDestinations.TryGetValue(wireId, out destination);
    }

    public Position? DestinationOf(string wireId)
    {
        lock (_gate)
        {
            if (_routeDestinations.TryGetValue(wireId, out var route)) return route;
            if (_returning.TryGetValue(wireId, out var leg)) return leg.Destination;
            return null;
        }
    }

    public FollowUpdate? AdvanceFollow(
        string wireId, Position current, Position leaderPosition, Position? leaderDestination,
        CreatureSpawnDefinition spawn, CreatureSpawnDefinition leaderSpawn, DateTimeOffset now)
    {
        lock (_gate) return _follow.Advance(wireId, current, leaderPosition, leaderDestination, spawn, leaderSpawn, now);
    }
}

// Every session hands over the same content list, so the per-map, id-ordered
// slice is worked out once instead of on every visibility scan.
internal sealed class SpawnsOnMap<T>
{
    private object? _source;
    private int _sourceCount = -1;
    private T[] _onMap = [];

    public T[] For(IEnumerable<T> spawns, ushort mapId, Func<T, ushort> mapOf, Func<T, uint> idOf)
    {
        var count = spawns is IReadOnlyCollection<T> collection ? collection.Count : -1;
        if (ReferenceEquals(spawns, _source) && count >= 0 && count == _sourceCount) return _onMap;
        _onMap = spawns.Where(s => mapOf(s) == mapId).OrderBy(idOf).ToArray();
        _source = spawns;
        _sourceCount = count;
        return _onMap;
    }
}

public sealed class SharedMapRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<ushort, SharedMapState> _maps = new();
    private readonly Func<double>? _rollUnit;
    private readonly TimeProvider? _time;

    public SharedMapRegistry(Func<double>? rollUnit = null, TimeProvider? time = null)
    {
        _rollUnit = rollUnit;
        _time = time;
    }

    public IReadOnlyList<SharedMapState> Live
    {
        get { lock (_gate) return [.. _maps.Values]; }
    }

    public SharedMapState For(
        ushort mapId,
        Func<float, float, float?, GroundAnswer?>? groundHeight = null,
        Func<Position, Position, bool>? canWalkBetween = null)
    {
        lock (_gate)
        {
            if (!_maps.TryGetValue(mapId, out var state))
            {
                state = new SharedMapState(mapId, _rollUnit, groundHeight, canWalkBetween, _time);
                _maps[mapId] = state;
            }
            return state;
        }
    }
}
