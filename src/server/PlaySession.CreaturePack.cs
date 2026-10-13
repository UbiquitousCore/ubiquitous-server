using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void EmitJoinResponses(List<string> responses, string joinerId, string playerId)
    {
        responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString(joinerId), WebNetworkId.FromString(playerId))));
        responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
            new AttackEnterCombatPayload(WebNetworkId.FromString(joinerId))));
        responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
            new AttackEnterCombatPayload(WebNetworkId.FromString(playerId))));
    }

    private void TickPackAssist(DateTimeOffset now, List<string> responses)
    {
        if (_character is null) return;
        var target = _combat.CurrentTargetOf(PlayerUnitId);
        if (target is null || BodyGone(target)) return;
        PullPackOf(target, now, responses);
    }

    private void PullPackOf(string mobId, DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _shared is null || _world is null) return;
        var playerId = PlayerUnitId;
        if (!_sight.Contains(mobId)) return;
        if (!ScriptFor(mobId).DrawsOthersIn) return;
        if (PackRootOfWire(mobId) is not uint packRoot) return;
        foreach (var wireId in _sight.Ids)
        {
            if (string.Equals(wireId, mobId, StringComparison.Ordinal)) continue;
            if (!AnswersPackCall(wireId, packRoot, now)) continue;
            _combat.EnlistAttacker(wireId, playerId, now);
            NotePackJoiner(wireId);
            HoldEngaged(wireId);
            _shared.ClearReturning(wireId);
            EmitJoinResponses(responses, wireId, playerId);
        }
    }

    private bool AnswersPackCall(string wireId, uint packRoot, DateTimeOffset now)
    {
        if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)
            || !_spawnById.TryGetValue(spawnId, out var spawn)
            || PackRootOf(spawnId) != packRoot)
            return false;
        if (!SimulatesNow(wireId, spawn, now) || BodyGone(wireId)) return false;
        if (IsReturningHome(wireId)) return false;

        _sight.Entries.TryGetValue(wireId, out var entry);
        bool fightsForItsGroup = IsHostile(CreatureFor(wireId))
            && !IsServiceNpc(entry)
            && ScriptFor(wireId).NoticesEnemies;
        bool free = _combat.CurrentTargetOf(wireId) is null && !_shared!.IsEngaged(wireId);
        return fightsForItsGroup && free;
    }

    private uint PackRootOf(uint spawnId) =>
        _spawnById.TryGetValue(spawnId, out var spawn) && spawn.LeaderSpawnId != 0
            ? spawn.LeaderSpawnId
            : spawnId;

    private uint? PackRootOfWire(string wireId) =>
        _sight.SpawnIds.TryGetValue(wireId, out var spawnId) ? PackRootOf(spawnId) : null;

    private bool LeaderStillFighting(string wireId)
    {
        if (_shared is null) return false;
        if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)) return false;
        var root = PackRootOf(spawnId);
        if (root == spawnId) return false;
        if (!_sight.WireIdsBySpawnId.TryGetValue(root, out var leaderWireId)) return false;
        return !BodyGone(leaderWireId) && _shared.IsEngaged(leaderWireId);
    }

    private bool LeadsPackOf(string leaderWireId, string followerWireId) =>
        _sight.SpawnIds.TryGetValue(leaderWireId, out var leaderSpawn)
        && _sight.SpawnIds.TryGetValue(followerWireId, out var followerSpawn)
        && leaderSpawn != followerSpawn
        && PackRootOf(followerSpawn) == leaderSpawn;

    private void StandDownPack(List<string>? responses)
    {
        if (_character is null) return;
        var currentTarget = _combat.CurrentTargetOf(PlayerUnitId);
        foreach (var joinerId in _packJoiners.ToArray())
        {
            if (string.Equals(joinerId, currentTarget, StringComparison.Ordinal)) continue;
            StandDownJoiner(joinerId, responses);
        }
    }

    private void StandDownJoiner(string joinerId, List<string>? responses)
    {
        _packJoiners.Remove(joinerId);
        _outOfMeleeRangeSince.Remove(joinerId);
        _combat.ReleaseJoiner(joinerId);
        ReleaseEngaged(joinerId);
        if (_shared is not null && !_shared.IsEngaged(joinerId))
        {
            _combat.ClearHostility(joinerId);
            _shared.ClearFighters(joinerId);
            MarkReturning(joinerId);
        }
        if (responses is null || !_sight.Contains(joinerId)) return;
        FaceHome(joinerId, responses);
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(joinerId))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat,
            new AttackExitCombatPayload(WebNetworkId.FromString(joinerId))));
    }

    private readonly HashSet<string> _packJoiners = new(StringComparer.Ordinal);

    private void NotePackJoiner(string wireId) => _packJoiners.Add(wireId);

    private void GuardReach(string guardId, string mobId, DateTimeOffset now)
    {
        if (!_shared!.TryGetPosition(guardId, out var guardPosition)
            || !_shared.TryGetPosition(mobId, out var mobPosition)) return;
        var dx = mobPosition.X - guardPosition.X;
        var dz = mobPosition.Z - guardPosition.Z;
        var distance = Math.Sqrt(dx * dx + dz * dz);
        _combat.UpdateStance(guardId, new RangedAttackContext(
            distance <= MeleeRange, distance, false, RangedAttackInterval, false), now);
    }

    private bool HomeFor(string wireId, out DomainPosition home)
    {
        home = default;
        if (!_sight.SpawnIds.TryGetValue(wireId, out var spawnId)
            || !_spawnById.TryGetValue(spawnId, out var spawn)) return false;
        home = new DomainPosition(spawn.X, spawn.Y, spawn.Z);
        return true;
    }

    private void MarkReturning(string wireId)
    {
        if (_shared is null || !HomeFor(wireId, out var home)) return;
        _shared.MarkReturning(wireId, home);
        ResetForHome(wireId);
    }

    private void MarkFightsReturning()
    {
        if (_shared is null || _character is null) return;
        var playerId = PlayerUnitId;
        var bodies = new HashSet<string>(_packJoiners, StringComparer.Ordinal);
        if (_combat.CurrentTargetOf(playerId) is { } opponent) bodies.Add(opponent);
        foreach (var (guardId, mobId) in _helperFights)
        {
            bodies.Add(guardId);
            bodies.Add(mobId);
        }

        foreach (var wireId in _sight.Entries.Keys)
        {
            if (string.Equals(_combat.RetaliationTarget(wireId), playerId, StringComparison.Ordinal))
                bodies.Add(wireId);
        }

        ReleaseAllEngaged();
        foreach (var wireId in bodies)
        {
            if (!_shared.IsEngaged(wireId)) MarkReturning(wireId);
        }
    }
}
