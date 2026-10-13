using Microsoft.Extensions.Logging;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void TickDungeonCompletion(List<string> responses)
    {
        if (_instanceWorld is null || _instanceKey is null || _character is null) return;
        if (_instances.IsCompleted(_instanceKey)) return;

        var inView = new HashSet<uint>();
        var dead = new HashSet<uint>();
        var living = new HashSet<uint>();
        foreach (var (wireId, seen) in _sight.Entries)
        {
            var entry = _copy.AuthoredEntry(seen);
            inView.Add(entry);
            if (_combat.IsDead(wireId)) dead.Add(entry); else living.Add(entry);
        }
        dead.ExceptWith(living);

        var progress = _scripts.ForMap(_instanceWorld.MapId)
            .Progress(new DungeonProgressContext(
                _instanceWorld.MapId, inView, dead, _instances.ReachedBy(_instanceKey)));

        foreach (var milestone in progress.Reached)
        {
            if (string.Equals(milestone, InstanceService.RunCompleted, StringComparison.Ordinal)) continue;
            if (!_instances.Reach(_instanceKey, milestone)) continue;
            AnnounceBarriersOpenedBy(milestone, responses);
        }

        if (SummonDueCreatures(responses)) return;

        if (!progress.Completed) return;
        _instances.MarkCompleted(_instanceKey);
        responses.Add(Reply(WebMessageTypes.DungeonCompleted,
            new DungeonCompletedPayload(_instanceWorld.MapId, progress.Name)));
    }

    private void AnnounceBarriersOpenedBy(string milestone, List<string> responses)
    {
        if (_world is null) return;
        _logger.LogInformation("Dungeon milestone reached on map {MapId}: {Milestone}", _world.MapId, milestone);
        foreach (var barrier in _content.DungeonBarriers)
        {
            if (barrier.MapId != _world.MapId) continue;
            if (!string.Equals(barrier.OpenedBy, milestone, StringComparison.Ordinal)) continue;
            _logger.LogInformation(
                "Way opened: {Barrier} (x {MinX}..{MaxX}, z {MinZ}..{MaxZ})",
                barrier.Id, barrier.MinX, barrier.MaxX, barrier.MinZ, barrier.MaxZ);
            responses.Add(Reply(WebMessageTypes.DungeonBarrierOpened,
                new DungeonBarrierOpenedPayload(barrier.Id, Announce: true,
                    barrier.MinX, barrier.MaxX, barrier.MinZ, barrier.MaxZ,
                    barrier.ModelNames)));
        }
    }

    private bool SummonDueCreatures(List<string> responses)
    {
        if (_instanceWorld is null || _instanceKey is null || _shared is null) return false;

        var created = false;
        foreach (var summon in _content.DungeonSummons)
        {
            if (summon.MapId != _instanceWorld.MapId) continue;

            var outstanding = summon.After
                .Where(milestone => !_instances.HasReached(_instanceKey, milestone))
                .ToArray();
            if (outstanding.Length > 0)
            {
                ReportSummonWaiting(summon.CreatureEntryId, outstanding);
                continue;
            }

            ReportSummonWaiting(summon.CreatureEntryId, []);
            var spawn = _copy.Spawn(new CreatureSpawnDefinition(
                SummonSpawnId(summon), _instanceWorld.MapId, summon.CreatureEntryId,
                summon.X, summon.Y, summon.Z, summon.Yaw));
            if (_instanceWorld.Summon(spawn) is null) continue;

            _spawnById[spawn.Id] = spawn;
            _logger.LogInformation(
                "Dungeon boss created on map {MapId}: entry {Entry} at ({X}, {Y}, {Z})",
                summon.MapId, spawn.CreatureEntryId, spawn.X, spawn.Y, spawn.Z);
            created = true;
        }

        return AdoptSummons(responses) || created;
    }

    private bool AdoptSummons(List<string> responses)
    {
        if (_instanceWorld is null || _shared is null) return false;
        var cursor = _summonCursor;
        var summoned = _instanceWorld.SummonedSince(ref cursor);
        _summonCursor = cursor;

        var adopted = false;
        foreach (var entity in summoned)
        {
            var wireId = entity.Id.ToString();
            if (_sight.Contains(wireId)) continue;
            var home = new DomainPosition(entity.Position.X, entity.Position.Y, entity.Position.Z);
            _shared.Adopt(wireId, entity.SpawnId, home);
            if (!_shared.IsAlive(wireId)) continue;
            _shared.SetPosition(wireId, home);
            RegisterCreature(entity);
            responses.Add(SpawnedReply(entity));
            adopted = true;
        }

        return adopted;
    }

    private void ReportSummonWaiting(uint creatureEntryId, IReadOnlyList<string> outstanding)
    {
        var summary = outstanding.Count == 0 ? "nothing" : string.Join(", ", outstanding);
        if (_summonWaiting.TryGetValue(creatureEntryId, out var last)
            && string.Equals(last, summary, StringComparison.Ordinal)) return;

        _summonWaiting[creatureEntryId] = summary;
        if (outstanding.Count == 0)
            _logger.LogDebug("Dungeon summon ready: entry {Entry} is waiting on nothing", creatureEntryId);
        else
            _logger.LogDebug("Dungeon summon waiting: entry {Entry} still needs {Outstanding}", creatureEntryId, summary);
    }

    private readonly Dictionary<uint, string> _summonWaiting = [];

    private static uint SummonSpawnId(DungeonSummonDefinition summon) =>
        SummonedSpawnIdBase + (summon.MapId * 100_000u) + summon.CreatureEntryId;

    private const uint SummonedSpawnIdBase = 1_000_000_000;

    private string HiddenObjectsReply(ushort mapId)
    {
        var positions = _content.HiddenStaticObjects
            .Where(spawn => spawn.MapId == mapId)
            .Select(spawn => new Position(spawn.X, spawn.Y, spawn.Z))
            .ToArray();
        return Reply(WebMessageTypes.WorldHiddenObjects, new WorldHiddenObjectsPayload(positions));
    }

    private IEnumerable<string> OpenBarrierReplies(ushort mapId)
    {
        if (_instanceKey is null) yield break;
        foreach (var barrier in _content.DungeonBarriers)
        {
            if (barrier.MapId != mapId) continue;
            if (!_instances.HasReached(_instanceKey, barrier.OpenedBy)) continue;
            yield return Reply(WebMessageTypes.DungeonBarrierOpened,
                new DungeonBarrierOpenedPayload(barrier.Id, Announce: false,
                    barrier.MinX, barrier.MaxX, barrier.MinZ, barrier.MaxZ,
                    barrier.ModelNames));
        }
    }
}
