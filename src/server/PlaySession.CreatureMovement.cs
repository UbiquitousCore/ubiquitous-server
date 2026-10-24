using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void DrainMapMovements(List<string> responses)
    {
        if (_shared is null) return;
        foreach (var movement in _shared.MovementsSince(_movementCursor, _sight.Contains, out _movementCursor))
        {
            _sight.Move(movement.WireId, movement.Position);

            responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
                NetworkId: WebNetworkId.FromString(movement.WireId),
                Position: new Position(movement.Position.X, movement.Position.Y, movement.Position.Z),
                Yaw: movement.Target is { } facing ? FacingYaw(movement.Position, facing) : HomeYawOf(movement.WireId),
                Moving: movement.Moving,
                Speed: movement.Speed,
                Target: movement.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
        }
    }

    private long _movementCursor;


    private double? HomeYawOf(string wireId) =>
        _sight.SpawnIds.TryGetValue(wireId, out var spawnId) && _spawnById.TryGetValue(spawnId, out var spawn)
            ? spawn.Yaw
            : null;

    private void TickChase(DateTimeOffset now, List<string> responses)
    {
        if (_character is null) return;
        var playerId = PlayerUnitId;
        var currentTarget = _combat.CurrentTargetOf(playerId);
        if (currentTarget is not null && _sight.Contains(currentTarget)
            && ScriptFor(currentTarget).Chases
            && _shared is not null && _shared.TryGetPosition(currentTarget, out var creaturePosition)
            && _combat.RetaliationTarget(currentTarget) is { } victim)
        {
            DomainPosition destination;
            if (string.Equals(victim, playerId, StringComparison.Ordinal))
            {
                var playerPosition = _world!.PlayerPosition;
                destination = new DomainPosition(playerPosition.X, playerPosition.Y, playerPosition.Z);
            }
            else if (!_shared.TryGetPosition(victim, out destination)) return;
            ChaseTowards(currentTarget, creaturePosition, destination, now, responses);
        }

        if (_shared is not null && _world is not null)
        {
            var playerPosition = _world.PlayerPosition;
            var playerDestination = new DomainPosition(playerPosition.X, playerPosition.Y, playerPosition.Z);
            foreach (var wireId in _sight.Ids)
            {
                if (string.Equals(wireId, currentTarget, StringComparison.Ordinal)) continue;
                if (_helperFights.ContainsKey(wireId)) continue;
                if (BodyGone(wireId)) continue;
                if (!string.Equals(_combat.RetaliationTarget(wireId), playerId, StringComparison.Ordinal)) continue;
                if (!ScriptFor(wireId).Chases) continue;
                if (!_shared.TryGetPosition(wireId, out var joinerPosition)) continue;
                var jdx = playerDestination.X - joinerPosition.X;
                var jdz = playerDestination.Z - joinerPosition.Z;
                _combat.SetInMeleeReach(wireId, Math.Sqrt(jdx * jdx + jdz * jdz) <= MeleeRange);
                ChaseTowards(wireId, joinerPosition, playerDestination, now, responses);
            }
        }

        foreach (var (guardId, mobId) in _helperFights.ToArray())
        {
            if (!_sight.Contains(mobId) || !_sight.Contains(guardId)) continue;
            if (!ScriptFor(mobId).Chases) continue;
            if (_shared is null || !_shared.TryGetPosition(mobId, out var mobPosition)) continue;
            if (!_shared.TryGetPosition(guardId, out var guardPosition)) continue;
            ChaseTowards(guardId, guardPosition, mobPosition, now, responses);
        }
    }

    private IReadOnlyList<DomainPosition>? ChaseRoute(DomainPosition from, DomainPosition to) =>
        _world is null ? null : _walkableGround?.FindRoute(_world.MapId, from, to);

    private void ChaseTowards(string chaserId, DomainPosition chaserPosition, DomainPosition destination, DateTimeOffset now, List<string> responses)
    {
        if (_control.IsControlled(chaserId, now) || IsHoldingStill(chaserId, now))
        {
            _chase.Clear(chaserId);
            return;
        }
        var pace = SpeedMultiplierOf(chaserId, now);
        var update = _chase.Advance(chaserId, chaserPosition, destination, MeleeRange, now, pace);
        if (update is null || _shared is null) return;

        _shared.SetPosition(chaserId, update.Position);
        _sight.Move(chaserId, update.Position);

        responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
            NetworkId: WebNetworkId.FromString(chaserId),
            Position: new Position(update.Position.X, update.Position.Y, update.Position.Z),
            Yaw: FacingYaw(update.Position, destination),
            Moving: update.Moving,
            Speed: CreatureChaseService.Speed * pace,
            Target: update.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
    }

    private readonly record struct IdleWalker(string WireId, CreatureSpawnDefinition Spawn, string? Leader);

    // Patrollers walk first so their followers keep pace with where they are now.
    private void TickIdleMovement(DateTimeOffset now, List<string> responses)
    {
        if (_shared is null) return;
        var playerTarget = _character is null ? null : _combat.CurrentTargetOf(PlayerUnitId);
        List<IdleWalker> patrollers = [], followers = [], wanderers = [];
        foreach (var wireId in _sight.Ids)
        {
            if (BodyGone(wireId)) continue;
            if (string.Equals(playerTarget, wireId, StringComparison.Ordinal)) continue;
            if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)
                || !_spawnById.TryGetValue(spawnId, out var spawn)) continue;
            if (_shared.IsEngaged(wireId) || _shared.IsReturning(wireId)) continue;
            var leader = LeaderOf(wireId);
            if (leader is not null) followers.Add(new IdleWalker(wireId, spawn, leader));
            else if (spawn.PatrolRouteId != 0) patrollers.Add(new IdleWalker(wireId, spawn, null));
            else if (spawn.IdleBehaviour == 1) wanderers.Add(new IdleWalker(wireId, spawn, null));
        }

        foreach (var walker in patrollers) WalkRoute(walker, now, responses);
        foreach (var walker in followers) Follow(walker, now, responses);
        foreach (var walker in wanderers) Wander(walker, now, responses);
    }

    private void WalkRoute(IdleWalker walker, DateTimeOffset now, List<string> responses)
    {
        var (wireId, spawn, _) = walker;
        if (!_shared!.TryGetPosition(wireId, out var current)) return;
        var route = _patrols.TryGet(spawn.PatrolRouteId, out var placed)
            ? placed
            : _shared.PatrolFor(spawn);
        if (!SimulatesNow(wireId, spawn, now)) return;

        var update = _shared.AdvanceRoute(wireId, current, route, now);
        if (update is null) return;

        _shared.SetPosition(wireId, update.Position);
        _sight.Move(wireId, update.Position);

        if (!update.IsTransition) return;
        responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
            NetworkId: WebNetworkId.FromString(wireId),
            Position: new Position(update.Position.X, update.Position.Y, update.Position.Z),
            Yaw: update.Target is { } routeDestination ? FacingYaw(update.Position, routeDestination) : null,
            Moving: update.Moving,
            Speed: CreatureWaypointRouteService.Speed,
            Target: update.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
    }

    private string? LeaderOf(string wireId)
    {
        if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)
            || !_spawnById.TryGetValue(spawnId, out var spawn)) return null;
        if (spawn.LeaderSpawnId == 0) return null;
        if (!_sight.WireIdsBySpawnId.TryGetValue(spawn.LeaderSpawnId, out var leaderWireId)) return null;
        if (!_sight.Contains(leaderWireId)) return null;
        if (!_sight.SpawnIds.TryGetValue(leaderWireId, out var leaderSpawnId)
            || !_spawnById.TryGetValue(leaderSpawnId, out var leaderSpawn)) return null;
        return leaderSpawn.PatrolRouteId == 0 ? null : leaderWireId;
    }

    private void Follow(IdleWalker walker, DateTimeOffset now, List<string> responses)
    {
        var (wireId, spawn, leaderWireId) = walker;
        if (!_shared!.TryGetPosition(wireId, out var current)) return;
        if (!_shared.TryGetPosition(leaderWireId!, out var leaderPosition)) return;
        if (!_sight.SpawnIds.TryGetValue(leaderWireId!, out var leaderSpawnId)
            || !_spawnById.TryGetValue(leaderSpawnId, out var leaderSpawn)) return;
        if (!SimulatesNow(wireId, spawn, now)) return;

        DomainPosition? leaderDestination =
            _shared.TryGetRouteDestination(leaderWireId!, out var destination) ? destination : null;

        var update = _shared.AdvanceFollow(
            wireId, current, leaderPosition, leaderDestination, spawn, leaderSpawn, now);
        if (update is null) return;

        _shared.SetPosition(wireId, update.Position);
        _sight.Move(wireId, update.Position);

        if (!update.IsTransition) return;
        responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
            NetworkId: WebNetworkId.FromString(wireId),
            Position: new Position(update.Position.X, update.Position.Y, update.Position.Z),
            Yaw: update.Target is { } followDestination ? FacingYaw(update.Position, followDestination) : null,
            Moving: update.Moving,
            Speed: CreatureFollowService.Speed,
            Target: update.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
    }

    private void Wander(IdleWalker walker, DateTimeOffset now, List<string> responses)
    {
        var (wireId, spawn, _) = walker;
        if (!_shared!.TryGetPosition(wireId, out var current)) return;
        var home = new DomainPosition(spawn.X, spawn.Y, spawn.Z);
        if (!SimulatesNow(wireId, spawn, now)) return;
        var update = _shared.AdvanceWander(wireId, current, home, spawn.WanderRadius, now);
        if (update is null) return;

        _shared.SetPosition(wireId, update.Position);
        _sight.Move(wireId, update.Position);

        if (!update.IsTransition) return;
        responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
            NetworkId: WebNetworkId.FromString(wireId),
            Position: new Position(update.Position.X, update.Position.Y, update.Position.Z),
            Yaw: update.Target is { } wanderDestination ? FacingYaw(update.Position, wanderDestination) : null,
            Moving: update.Moving,
            Speed: CreatureWanderService.Speed,
            Target: update.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
    }
}
