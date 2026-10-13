using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.World;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void TickDespawns(DateTimeOffset now, List<string> responses)
    {
        foreach (var (targetId, held) in _despawnDue.ToArray())
        {
            var due = SharedCorpseUntil(targetId) ?? held;
            if (now < due) continue;
            _despawnDue.Remove(targetId);
            if (_shared is not null && _shared.IsAlive(targetId)) continue;
            if (_shared is not null && _sight.SpawnIds.TryGetValue(targetId, out var spawnId)
                && _shared.TryGetWireId(spawnId, out var currentWire)
                && !string.Equals(currentWire, targetId, StringComparison.Ordinal))
            {
                Forget(targetId);
                responses.Add(Reply(WebMessageTypes.EntityRemoved,
                    new EntityRemovedPayload(WebNetworkId.FromString(targetId))));
                continue;
            }
            if (_shared is not null && _sight.SpawnIds.TryGetValue(targetId, out var deadSpawn))
                _shared.ScheduleRespawn(deadSpawn, RespawnDelayFor(targetId), now);
            Forget(targetId);
            responses.Add(Reply(WebMessageTypes.EntityRemoved,
                new EntityRemovedPayload(WebNetworkId.FromString(targetId))));
        }
    }

    private TimeSpan RespawnDelayForEntry(uint entry)
    {
        if (!_content.Creatures.TryGetValue(entry, out var creature)
            || creature.RespawnTimeMinSeconds is not int min || creature.RespawnTimeMaxSeconds is not int max)
            return RespawnDelay;
        return TimeSpan.FromSeconds(min >= max ? min : Random.Shared.Next(min, max + 1));
    }

    private IReadOnlyList<string> EnterWorldCorpseReplies(ushort mapId)
    {
        var responses = new List<string>();
        if (_shared is null || _world is null) return responses;
        var now = Now;
        var range = WorldSessionState.DefaultViewRange;
        var rangeSquared = range * range;
        foreach (var spawn in _creatureSpawns)
        {
            if (spawn.MapId != mapId) continue;
            DomainPosition anchor = new(spawn.X, spawn.Y, spawn.Z);
            if (_shared.TryGetWireId(spawn.Id, out var knownWire)
                && _shared.TryGetPosition(knownWire, out var knownPos))
                anchor = knownPos;
            var dx = anchor.X - _world.PlayerPosition.X;
            var dz = anchor.Z - _world.PlayerPosition.Z;
            if (dx * dx + dz * dz > rangeSquared) continue;
            var delay = RespawnDelayForEntry(spawn.CreatureEntryId);
            _shared.ScheduleRespawnIfAbsent(spawn.Id, delay, CorpseLifetime.WithLoot, now);
            if (!_shared.TryGetCorpseUntil(spawn.Id, out var until) || now >= until) continue;
            if (!_shared.TryGetWireId(spawn.Id, out var wireId)
                || _sight.Contains(wireId)
                || !_shared.TryGetPosition(wireId, out var corpsePos)) continue;
            var entity = new Ubiquitous.World.WorldEntitySnapshot(
                Ubiquitous.Common.EntityId.Parse(wireId), _copy.CreatureEntry(spawn.CreatureEntryId), corpsePos, spawn.Yaw, spawn.Id);
            RegisterCreature(entity);
            responses.Add(SpawnedReply(entity, dead: true));
            responses.Add(Reply(WebMessageTypes.EntityLootable,
                new EntityLootablePayload(WebNetworkId.FromString(wireId), WireCodes.Loot.Emptied)));
            _combat.MarkDead(wireId);
            _despawnDue[wireId] = until;
        }
        return responses;
    }

    private TimeSpan RespawnDelayFor(string targetId)
    {
        if (!_sight.Entries.TryGetValue(targetId, out var entry)
            || !_content.Creatures.TryGetValue(entry, out var creature)
            || creature.RespawnTimeMinSeconds is not int min || creature.RespawnTimeMaxSeconds is not int max)
            return RespawnDelay;
        return TimeSpan.FromSeconds(min >= max ? min : Random.Shared.Next(min, max + 1));
    }

    private void TickRespawns(DateTimeOffset now, List<string> responses)
    {
        if (_instanceWorld is not null)
        {
            if (_shared is null) return;
            foreach (var spawnId in _instanceWorld.DueRespawns(now))
            {
                if (!_instanceWorld.TryGetHomeSnapshot(spawnId, out var entity))
                {
                    _instanceWorld.ConfirmRespawned(spawnId);
                    continue;
                }
                var wireId = entity.Id.ToString();
                if (_sight.Contains(wireId)) continue;
                _instanceWorld.ConfirmRespawned(spawnId);
                _shared.Revive(spawnId);
                var home = new DomainPosition(entity.Position.X, entity.Position.Y, entity.Position.Z);
                _shared.Adopt(wireId, spawnId, home);
                _shared.SetPosition(wireId, home);
                RegisterCreature(entity);
                _combat.ClearDeath(wireId);
                responses.Add(SpawnedReply(entity));
            }
            return;
        }
        if (_shared is null || _world is null) return;
        _shared.CollectDueRespawns(now);

        foreach (var entity in _shared.VisibleCreatures(_world.PlayerPosition, WorldSessionState.DefaultViewRange, _creatureSpawns))
        {
            if (_sight.Contains(entity.Id.ToString())) continue;
            RegisterCreature(entity);
            responses.Add(SpawnedReply(entity));
        }
    }

    private void RefreshView(List<string> responses)
    {
        if (_shared is null || _world is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in _shared.VisibleCreatures(_world.PlayerPosition, WorldSessionState.DefaultViewRange, _creatureSpawns))
        {
            var wireId = entity.Id.ToString();
            seen.Add(wireId);
            if (_sight.Contains(wireId)) continue;
            RegisterCreature(entity);
            responses.Add(SpawnedReply(entity));
        }
        foreach (var entity in _shared.VisibleStaticObjects(_world.PlayerPosition, WorldSessionState.DefaultViewRange, _staticObjectSpawns))
        {
            var wireId = entity.Id.ToString();
            seen.Add(wireId);
            if (_visibleStaticObjectEntries.TryGetValue(wireId, out var shownEntry))
            {
                if (shownEntry == entity.Entry) continue;
                responses.Add(Reply(WebMessageTypes.EntityRemoved,
                    new EntityRemovedPayload(WebNetworkId.FromString(wireId))));
            }

            RegisterStaticObject(entity);
            responses.Add(SpawnedReply(entity));
        }
        foreach (var wireId in _sight.Entries.Keys.Concat(_visibleStaticObjectEntries.Keys).ToArray())
        {
            if (seen.Contains(wireId) || _despawnDue.ContainsKey(wireId)) continue;
            Forget(wireId);
            responses.Add(Reply(WebMessageTypes.EntityRemoved,
                new EntityRemovedPayload(WebNetworkId.FromString(wireId))));
        }
    }

    private void RemoveView(List<string> responses)
    {
        foreach (var wireId in _sight.Entries.Keys.Concat(_visibleStaticObjectEntries.Keys))
        {
            responses.Add(Reply(WebMessageTypes.EntityRemoved,
                new EntityRemovedPayload(WebNetworkId.FromString(wireId))));
        }
    }

    private void ResetVisibility()
    {
        EndAllHunts();
        UnmarkEngaged();
        StandDownPack(null);
        MarkFightsReturning();
        ClearGuardFights();
        _announcedMobVictims.Clear();
        _pendingLeaveTarget = null;
        _sight.Clear();
        ClearStressBots(null);
        _visibleStaticObjectEntries.Clear();
        _staticObjectPositions.Clear();
        _gathering.Cancel();
        _spellFlights.Clear();
        _gatheredLoot.Clear();
        _outOfMeleeRangeSince.Clear();
        _playerMoving = false;
        _loot.Clear();
        _despawnDue.Clear();
        ClearEffectsOn(PlayerUnitId);
        ForgetReportedAuras();
        _auraStateSent.Clear();
        _dirty.Clear();
    }

    private void AppendCombatEntries(List<string> responses)
    {
        foreach (var entry in _combat.DrainCombatEntries())
        {
            responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
                WebNetworkId.FromString(entry.AttackerId), WebNetworkId.FromString(entry.TargetId))));
            if (!entry.EnteredCombat) continue;
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(entry.AttackerId))));
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(entry.TargetId))));
            if (_character is not null
                && string.Equals(entry.AttackerId, PlayerUnitId, StringComparison.Ordinal))
                PullPackOf(entry.TargetId, Now, responses);
        }
    }

    private CreatureCombatStats? CombatStatsFor(string targetId)
    {
        if (!_sight.Entries.TryGetValue(targetId, out var entry)
            || !_content.Creatures.TryGetValue(entry, out var creature))
            return null;

        var swingInterval = creature.MeleeAttackIntervalMs is int ms
            ? TimeSpan.FromMilliseconds(ms)
            : CombatService.SwingInterval;
        var minDamage = creature.MinDamage ?? CombatService.RetaliationDamage;
        var maxDamage = creature.MaxDamage ?? CombatService.RetaliationDamage;
        var maxHealth = creature.MaxHealth;
        return new CreatureCombatStats(
            maxHealth, minDamage, maxDamage, swingInterval,
            !_scripts.For(creature, _instanceWorld is not null, AdvertisesQuests(creature)).FightsBack,
            CreatureExtraFlags.IsTrainingDummy(creature));
    }

    private void RegisterCreature(WorldEntitySnapshot entity)
    {
        _sight.Add(entity);
    }

    private void Forget(string wireId)
    {
        _sight.Remove(wireId);
        _visibleStaticObjectEntries.Remove(wireId);
        _staticObjectPositions.Remove(wireId);
        _outOfMeleeRangeSince.Remove(wireId);
        _announcedMobVictims.Remove(wireId);
        _chase.Clear(wireId);
        _loot.Remove(wireId);
        _despawnDue.Remove(wireId);
        ClearEffectsOn(wireId);
        _shared?.Auras.Forget(this, wireId);
        _auraStateSent.Remove(wireId);
    }

    private void RegisterStaticObject(WorldEntitySnapshot entity)
    {
        var wireId = entity.Id.ToString();
        if (_gatheredLoot.Remove(wireId)) _loot.Remove(wireId);
        _visibleStaticObjectEntries[wireId] = entity.Entry;
        _staticObjectPositions[wireId] = entity.Position;
    }

    private string SpawnedReply(WorldEntitySnapshot entity, bool dead = false)
    {
        var isStaticObject = entity.Id.Type == Ubiquitous.Common.ObjectType.StaticObject;
        var creature = !isStaticObject && _content.Creatures.TryGetValue(entity.Entry, out var found) ? found : null;
        var payload = new EntitySpawnedPayload(
            WebNetworkId.FromString(entity.Id.ToString()), checked((int)entity.Entry),
            new Position(entity.Position.X, entity.Position.Y, entity.Position.Z), entity.Yaw,
            isStaticObject ? "staticObject" : null,
            NpcFlags: !isStaticObject && _content.Npcs.TryGetValue(entity.Entry, out var npc)
                ? (uint)npc.Flags : null,
            OffersQuests: !isStaticObject && _content.Npcs.TryGetValue(entity.Entry, out var giver) && giver.OffersQuests
                ? true : null,
            StaticObjectType: isStaticObject && _content.StaticObjectTypesByEntry.TryGetValue(entity.Entry, out var staticType)
                ? staticType : null,
            CanInteract: isStaticObject ? true : null,
            Target: !isStaticObject && _shared?.DestinationOf(entity.Id.ToString()) is { } destination
                ? new Position(destination.X, destination.Y, destination.Z)
                : null,
            Level: creature?.Level,
            Health: creature?.MaxHealth,
            MaxHealth: creature?.MaxHealth,
            Faction: creature?.FactionType,
            AnimationId: !isStaticObject && _spawnById.TryGetValue(entity.SpawnId, out var spawn) && spawn.AnimationId != 0
                ? spawn.AnimationId : null,
            NeverFightsBack: creature is not null
                && !_scripts.For(creature, _instanceWorld is not null, AdvertisesQuests(creature)).FightsBack
                ? true : null);
        payload = dead ? payload with { Dead = true } : payload with { Auras = AurasWornBy(entity.Id.ToString()) };
        return Reply(WebMessageTypes.EntitySpawned, payload);
    }
}
