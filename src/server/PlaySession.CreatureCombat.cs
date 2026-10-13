using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void TickAggro(DateTimeOffset now, List<string> responses)
    {
        if (_world is null || _character is null || _combat.IsPlayerDead) return;
        var playerId = PlayerUnitId;
        if (_combat.IsDead(playerId)) return;
        if (_stealth.IsStealthed(playerId, now)) return;

        var claimed = _combat.CurrentTargetOf(playerId) is not null;
        foreach (var wireId in _sight.Ids)
        {
            if (BodyGone(wireId)) continue;
            if (IsReturningHome(wireId)) continue;
            if (_combat.CurrentTargetOf(wireId) is not null) continue;
            if (_shared?.IsInHunt(wireId) == true) continue;
            if (!ScriptFor(wireId).NoticesEnemies) continue;
            var creature = CreatureFor(wireId);
            if (!IsHostile(creature)) continue;
            if (_sight.SpawnIds.TryGetValue(wireId, out var aggroSpawnId)
                && _spawnById.TryGetValue(aggroSpawnId, out var aggroSpawn)
                && !SimulatesNow(wireId, aggroSpawn, now)) continue;
            _sight.Entries.TryGetValue(wireId, out var entry);
            if (AggroRangeFor(creature, IsServiceNpc(entry)) is not int aggroRange) continue;
            if (!InRangeOf(wireId, aggroRange)) continue;

            if (!claimed)
            {
                var result = _combat.Start(
                    playerId, wireId, _sight.Entries.Keys.ToHashSet(StringComparer.Ordinal), now, CombatStatsFor(wireId),
                    holdFireUntilStruck: true);
                if (result is null) return;
                HoldEngaged(result.TargetId);
                if (_shared is not null) _shared.ClearReturning(result.TargetId);
                _announcedMobVictims[result.TargetId] = playerId;
                responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
                    WebNetworkId.FromString(result.TargetId), WebNetworkId.FromString(playerId))));
                if (result.EnteredCombat)
                {
                    responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                        new AttackEnterCombatPayload(WebNetworkId.FromString(result.AttackerId))));
                    responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                        new AttackEnterCombatPayload(WebNetworkId.FromString(result.TargetId))));
                }
                claimed = true;
                continue;
            }

            if (_shared?.IsEngaged(wireId) == true) continue;
            _combat.EnlistAttacker(wireId, playerId, now);
            NotePackJoiner(wireId);
            HoldEngaged(wireId);
            if (_shared is not null) _shared.ClearReturning(wireId);
            EmitJoinResponses(responses, wireId, playerId);
        }
    }

    private void TickLeash(DateTimeOffset now, List<string> responses)
    {
        if (_world is null || _character is null) return;
        var playerId = PlayerUnitId;
        var currentTarget = _combat.CurrentTargetOf(playerId);
        foreach (var joinerId in _packJoiners.ToArray())
        {
            if (string.Equals(joinerId, currentTarget, StringComparison.Ordinal)) continue;
            if (currentTarget is not null && LeadsPackOf(joinerId, currentTarget)) continue;
            if (ScriptFor(joinerId).GivesUp(GiveUpContextFor(joinerId, now)))
                StandDownJoiner(joinerId, responses);
        }

        if (currentTarget is null) return;
        if (_duels.IsActiveOpponent(playerId, currentTarget)) return;
        if (_pvpFlags.IsFlaggedPair(playerId, currentTarget)) return;

        if (!ScriptFor(currentTarget).GivesUp(GiveUpContextFor(currentTarget, now))) return;

        _outOfMeleeRangeSince.Remove(currentTarget);
        UnmarkEngaged();
        StandDownGuardsFor(currentTarget, responses);
        StandDownPack(responses);
        if (_shared is not null && !_shared.IsEngaged(currentTarget))
        {
            MarkReturning(currentTarget);
            FaceHome(currentTarget, responses);
        }
        _combat.Disengage(playerId);
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(currentTarget))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat,
            new AttackExitCombatPayload(WebNetworkId.FromString(currentTarget))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat,
            new AttackExitCombatPayload(WebNetworkId.FromString(playerId))));
    }

    private TimeSpan? TimeOutsideMeleeRange(string targetId, DateTimeOffset now)
    {
        if (InMeleeRange(targetId))
        {
            _outOfMeleeRangeSince.Remove(targetId);
            return null;
        }

        if (!_outOfMeleeRangeSince.TryGetValue(targetId, out var since))
        {
            _outOfMeleeRangeSince[targetId] = now;
            return TimeSpan.Zero;
        }

        return now - since;
    }

    private bool BodyGone(string wireId) =>
        _combat.IsDead(wireId) || (_shared is not null && !_shared.IsAlive(wireId));

    private void KillShared(string wireId, DateTimeOffset now)
    {
        if (_shared is null || !_sight.SpawnIds.TryGetValue(wireId, out var spawnId)) return;
        _shared.Kill(spawnId, now);
        if (_instanceWorld is not null && _spawnById.TryGetValue(spawnId, out var spawn))
        {
            _instanceWorld.Kill(spawnId, now);
            if (!InDungeon)
                _instanceWorld.ScheduleRespawn(spawnId, RespawnDelayForEntry(spawn.CreatureEntryId), now);
        }
    }

    private bool InDungeon => _instanceWorld is not null && IsDungeonMap(_instanceWorld.MapId);

    private void HoldCorpse(string wireId, DateTimeOffset now, bool carriesLoot)
    {
        var until = CorpseLifetime.Until(now, carriesLoot, InDungeon);
        if (_shared is not null && _sight.SpawnIds.TryGetValue(wireId, out var spawnId))
        {
            _shared.HoldCorpse(spawnId, until);
            until = SharedCorpseUntil(wireId) ?? until;
        }
        _despawnDue[wireId] = until;
    }

    private DateTimeOffset? SharedCorpseUntil(string wireId) =>
        _shared is not null && _sight.SpawnIds.TryGetValue(wireId, out var spawnId)
        && _shared.TryGetCorpseUntil(spawnId, out var until)
            ? until
            : null;

    private void UnmarkEngaged()
    {
        if (_character is null || _shared is null) return;
        var target = _combat.CurrentTargetOf(PlayerUnitId);
        if (target is not null) ReleaseEngaged(target);
    }

    public void ReleaseSharedCombat()
    {
        EndAllHunts();
        UnmarkEngaged();
        StandDownPack(null);
        MarkFightsReturning();
        ClearGuardFights();
        if (_character is null) return;
        var playerId = PlayerUnitId;
        _combat.Disengage(playerId);
        if (_shared is null) return;
        foreach (var wireId in _sight.Ids) _shared.ForgetAttacker(wireId, playerId);
    }

    private string? _pendingLeaveTarget;

    private void NoteRemoteDeath(string wireId)
    {
        if (_character is not null && wireId.StartsWith("player:", StringComparison.Ordinal)
            && string.Equals(_combat.CurrentTargetOf(PlayerUnitId), wireId, StringComparison.Ordinal))
        {
            _pendingLeaveTarget = wireId;
            return;
        }
        if (!_sight.Contains(wireId) || _combat.IsDead(wireId)) return;
        _combat.MarkDead(wireId);
        _despawnDue[wireId] = SharedCorpseUntil(wireId)
            ?? CorpseLifetime.Until(Now, carriesLoot: false, InDungeon);
        if (_character is not null
            && string.Equals(_combat.CurrentTargetOf(PlayerUnitId), wireId, StringComparison.Ordinal))
            _pendingLeaveTarget = wireId;
    }

    private void LeaveDeadTarget(string targetId, List<string> responses)
    {
        if (_character is null) return;
        var playerId = PlayerUnitId;
        if (!string.Equals(_combat.CurrentTargetOf(playerId), targetId, StringComparison.Ordinal)) return;
        UnmarkEngaged();
        _combat.Disengage(playerId);
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(playerId))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(playerId))));
    }

    private static double FacingYaw(DomainPosition from, DomainPosition to) =>
        Math.Atan2(to.X - from.X, -(to.Z - from.Z));

    private CreatureScript ScriptFor(string wireId)
    {
        var creature = CreatureFor(wireId);
        return _scripts.For(creature, _instanceWorld is not null, AdvertisesQuests(creature));
    }

    private bool AdvertisesQuests(CreatureDefinition? creature) =>
        creature is not null
        && _content.Npcs.TryGetValue(creature.EntryId, out var npc)
        && npc.OffersQuests;

    private DungeonBarrierDefinition? ClosedBarrierCrossed(DomainPosition from, DomainPosition to)
    {
        if (_instanceWorld is null || _instanceKey is null || _world is null) return null;
        foreach (var barrier in _content.DungeonBarriers)
        {
            if (barrier.MapId != _world.MapId) continue;
            if (_instances.HasReached(_instanceKey, barrier.OpenedBy)) continue;
            if (barrier.Refuses(from.X, from.Z, to.X, to.Z)) return barrier;
        }

        return null;
    }

    private GiveUpContext GiveUpContextFor(string wireId, DateTimeOffset now)
    {
        var creature = CreatureFor(wireId);
        var toTarget = DistanceToPlayer(wireId) ?? 0;
        var fromHome = DistanceFromHome(wireId) ?? 0;
        var inFight = _fightStartedAt is { } started ? now - started : TimeSpan.Zero;
        return new GiveUpContext(creature, toTarget, fromHome, inFight,
            LeaderStillFighting(wireId), TimeOutsideMeleeRange(wireId, now));
    }

    private double? DistanceToPlayer(string wireId)
    {
        if (_world is null || _shared is null || !_shared.TryGetPosition(wireId, out var body)) return null;
        var player = _world.PlayerPosition;
        var dx = player.X - body.X;
        var dy = player.Y - body.Y;
        var dz = player.Z - body.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private double? DistanceFromHome(string wireId)
    {
        if (_shared is null || !HomeFor(wireId, out var home)) return null;
        if (!_shared.TryGetPosition(wireId, out var current)) return null;
        var dx = current.X - home.X;
        var dz = current.Z - home.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private DateTimeOffset? _fightStartedAt;

    private string? _fightTarget;

    private void FaceHome(string wireId, List<string> responses)
    {
        if (_shared is null) return;
        if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)
            || !_spawnById.TryGetValue(spawnId, out var spawn)) return;
        if (!_shared.TryGetPosition(wireId, out var current)) return;
        responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
            NetworkId: WebNetworkId.FromString(wireId),
            Position: new Position(current.X, current.Y, current.Z),
            Yaw: spawn.Yaw,
            Moving: false)));
    }

    private void StandDownGuard(string guardId, List<string> responses)
    {
        _combat.Disengage(guardId);
        if (_helperFights.TryGetValue(guardId, out var mobId)) ReleaseEngaged(mobId);
        _helperFights.Remove(guardId);
        MarkReturning(guardId);
        FaceHome(guardId, responses);
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(guardId))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(guardId))));
    }

    private void StandDownGuardsFor(string mobId, List<string> responses)
    {
        foreach (var (guardId, fightingId) in _helperFights.ToArray())
        {
            if (string.Equals(fightingId, mobId, StringComparison.Ordinal))
                StandDownGuard(guardId, responses);
        }
    }

    private void ClearGuardFights()
    {
        foreach (var (guardId, mobId) in _helperFights.ToArray())
        {
            _combat.Disengage(guardId);
            ReleaseEngaged(mobId);
        }
        _helperFights.Clear();
    }

    private void TickGuardAssist(DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _shared is null) return;
        var playerId = PlayerUnitId;
        var mobId = _combat.CurrentTargetOf(playerId);
        foreach (var (guardId, fightingId) in _helperFights.ToArray())
        {
            if (BodyGone(fightingId) || !_sight.Contains(fightingId)
                || !string.Equals(mobId, fightingId, StringComparison.Ordinal))
                StandDownGuard(guardId, responses);
            else GuardReach(guardId, fightingId, now);
        }

        if (mobId is null || !_sight.Contains(mobId) || BodyGone(mobId)) return;
        if (!ScriptFor(mobId).DrawsOthersIn) return;
        var shared = _shared;
        if (!shared.TryGetPosition(mobId, out var mobPosition)) return;
        foreach (var wireId in _sight.Ids)
        {
            if (_helperFights.ContainsKey(wireId) || _combat.CurrentTargetOf(wireId) is not null) continue;
            if (IsReturningHome(wireId) || IsReturningHome(mobId)) continue;
            if (!shared.TryGetPosition(wireId, out var guardPosition)) continue;
            var dx = mobPosition.X - guardPosition.X;
            var dz = mobPosition.Z - guardPosition.Z;
            if (!ScriptFor(wireId).Assists(
                new AssistContext(CreatureFor(wireId), Math.Sqrt(dx * dx + dz * dz)))) continue;
            var result = _combat.Start(wireId, mobId,
                _sight.Entries.Keys.ToHashSet(StringComparer.Ordinal), now, CombatStatsFor(mobId));
            if (result is null) continue;
            HoldEngaged(mobId);
            _shared?.ClearReturning(mobId);
            _helperFights[wireId] = mobId;
            responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
                WebNetworkId.FromString(result.AttackerId), WebNetworkId.FromString(result.TargetId))));
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(result.AttackerId))));
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(result.TargetId))));
        }
    }
}
