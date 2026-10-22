using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly Dictionary<string, (string Prey, DomainPosition Home)> _hunts = new(StringComparer.Ordinal);

    private const int MaxHunts = 4;

    private void TickHunts(DateTimeOffset now, List<string> responses)
    {
        if (_shared is null || _world is null || _character is null) return;
        var playerId = PlayerUnitId;
        foreach (var (hunterId, hunt) in _hunts.ToArray())
        {
            var preyId = hunt.Prey;
            if (BodyGone(hunterId) || !_sight.Contains(hunterId))
            {
                EndHunt(hunterId, sendHome: true, now, responses);
                continue;
            }
            if (!_sight.Contains(preyId) || BodyGone(preyId)
                || !string.Equals(_combat.CurrentTargetOf(hunterId), preyId, StringComparison.Ordinal)
                || IsPlayersFight(preyId, playerId))
            {
                EndHunt(hunterId, sendHome: true, now, responses);
                continue;
            }
            if (IsPlayersFight(hunterId, playerId))
            {
                EndHunt(hunterId, sendHome: false, now, responses);
                continue;
            }
            if (!_shared.TryGetPosition(hunterId, out var hunterPosition)
                || !_shared.TryGetPosition(preyId, out var preyPosition)) continue;
            var fromHome = GroundDistance(hunterPosition, hunt.Home);
            var context = new Ubiquitous.Scripts.GiveUpContext(CreatureFor(hunterId),
                GroundDistance(hunterPosition, preyPosition), fromHome, TimeSpan.Zero, LeaderStillFighting: false);
            if (ScriptFor(hunterId).GivesUp(context))
            {
                EndHunt(hunterId, sendHome: true, now, responses);
                continue;
            }
            GuardReach(hunterId, preyId, now);
            ChaseTowards(hunterId, hunterPosition, preyPosition, now, responses);
        }

        StartHunts(now, responses);
    }

    private void StartHunts(DateTimeOffset now, List<string> responses)
    {
        if (_shared is null || _hunts.Count >= MaxHunts) return;
        var quarry = new List<(string Id, CreatureDefinition Creature, DomainPosition Position)>();
        foreach (var (wireId, entry) in _sight.Entries)
        {
            var creature = CreatureFor(wireId);
            if (creature?.Family != CreatureFamilies.Critter) continue;
            if (IsServiceNpc(entry) || !FreeForAHunt(wireId)) continue;
            if (!_shared.TryGetPosition(wireId, out var position)) continue;
            quarry.Add((wireId, creature, position));
        }
        if (quarry.Count == 0) return;

        foreach (var (hunterId, entry) in _sight.Entries.ToArray())
        {
            if (_hunts.Count >= MaxHunts || quarry.Count == 0) return;
            var hunter = CreatureFor(hunterId);
            if (hunter is null || FactionOf(hunter) != UnitFactionTypes.Hostile) continue;
            if (IsServiceNpc(entry) || !FreeForAHunt(hunterId)) continue;
            var script = ScriptFor(hunterId);
            if (!script.NoticesEnemies || !script.Chases) continue;
            if (!_sight.SpawnIds.TryGetValue(hunterId, out var spawnId)
                || !_spawnById.TryGetValue(spawnId, out var spawn)
                || !SimulatesNow(hunterId, spawn, now)) continue;
            if (!_shared.TryGetPosition(hunterId, out var hunterPosition)) continue;

            int? nearest = null;
            var nearestDistance = double.MaxValue;
            for (var i = 0; i < quarry.Count; i++)
            {
                if (!CreatureHunting.Hunts(hunter, quarry[i].Creature)) continue;
                var distance = hunterPosition.DistanceSquaredTo(quarry[i].Position);
                var range = CreatureHunting.NoticeRange(hunter, quarry[i].Creature, DefaultAggroRange);
                if (distance > range * range || distance >= nearestDistance) continue;
                nearest = i;
                nearestDistance = distance;
            }
            if (nearest is not int picked) continue;

            var preyId = quarry[picked].Id;
            if (!BeginHunt(hunterId, preyId, new DomainPosition(spawn.X, spawn.Y, spawn.Z), now, responses)) continue;
            quarry.RemoveAt(picked);
        }
    }

    private bool BeginHunt(string hunterId, string preyId, DomainPosition home, DateTimeOffset now, List<string> responses)
    {
        if (_shared is null || !_shared.TryClaimHunt(hunterId, preyId)) return false;
        var result = _combat.Start(hunterId, preyId,
            _sight.Entries.Keys.ToHashSet(StringComparer.Ordinal), now, CombatStatsFor(preyId));
        if (result is null)
        {
            _shared.ReleaseHunt(hunterId);
            return false;
        }

        _hunts[hunterId] = (preyId, home);
        HoldEngaged(hunterId);
        HoldEngaged(preyId);
        responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString(hunterId), WebNetworkId.FromString(preyId))));
        responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
            new AttackEnterCombatPayload(WebNetworkId.FromString(hunterId))));
        responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
            new AttackEnterCombatPayload(WebNetworkId.FromString(preyId))));
        return true;
    }

    private bool FreeForAHunt(string wireId) =>
        !BodyGone(wireId)
        && !IsReturningHome(wireId)
        && _combat.CurrentTargetOf(wireId) is null
        && _shared is not null && !_shared.IsEngaged(wireId) && !_shared.IsInHunt(wireId);

    private bool IsPlayersFight(string wireId, string playerId) =>
        string.Equals(_combat.CurrentTargetOf(playerId), wireId, StringComparison.Ordinal)
        || (_shared is not null && _shared.Fighters(wireId).Any(fighter => fighter.StartsWith("player:", StringComparison.Ordinal)));

    private bool LandHuntKill(CombatSwing swing, DateTimeOffset now, List<string> responses)
    {
        string? hunterId = null;
        if (_hunts.TryGetValue(swing.AttackerId, out var hunt)
            && string.Equals(hunt.Prey, swing.TargetId, StringComparison.Ordinal))
            hunterId = swing.AttackerId;
        else if (_hunts.TryGetValue(swing.TargetId, out var fallen)
            && string.Equals(fallen.Prey, swing.AttackerId, StringComparison.Ordinal))
            hunterId = swing.TargetId;
        if (hunterId is null) return false;

        var dead = WebNetworkId.FromString(swing.TargetId);
        KillShared(swing.TargetId, now);
        HoldCorpse(swing.TargetId, now, carriesLoot: false);
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(dead)));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(dead)));
        EndHunt(hunterId, sendHome: true, now, responses);
        return true;
    }

    private void EndHunt(string hunterId, bool sendHome, DateTimeOffset now, List<string>? responses)
    {
        if (!_hunts.Remove(hunterId, out var hunt)) return;
        var preyId = hunt.Prey;
        if (string.Equals(_combat.CurrentTargetOf(hunterId), preyId, StringComparison.Ordinal))
            _combat.Disengage(hunterId);
        if (!InOwnPlayersFight(hunterId)) ReleaseEngaged(hunterId);
        if (!InOwnPlayersFight(preyId)) ReleaseEngaged(preyId);
        _announcedMobVictims.Remove(preyId);
        if (_shared is null) return;
        _shared.ReleaseHunt(hunterId);

        if (!BodyGone(hunterId) && !_shared.IsEngaged(hunterId))
        {
            if (sendHome)
            {
                _shared.MarkReturning(hunterId, hunt.Home);
                ResetForHome(hunterId);
            }
            responses?.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(hunterId))));
            responses?.Add(Reply(WebMessageTypes.AttackExitCombat,
                new AttackExitCombatPayload(WebNetworkId.FromString(hunterId))));
        }

        if (!BodyGone(preyId) && !_shared.IsEngaged(preyId) && _sight.Contains(preyId))
        {
            ResetForHome(preyId);
            responses?.Add(Reply(WebMessageTypes.AttackExitCombat,
                new AttackExitCombatPayload(WebNetworkId.FromString(preyId))));
        }
    }

    private bool InOwnPlayersFight(string wireId)
    {
        if (_character is null) return false;
        var playerId = PlayerUnitId;
        return string.Equals(_combat.CurrentTargetOf(playerId), wireId, StringComparison.Ordinal)
            || string.Equals(_combat.RetaliationTarget(wireId), playerId, StringComparison.Ordinal)
            || _packJoiners.Contains(wireId)
            || _helperFights.ContainsKey(wireId)
            || _helperFights.ContainsValue(wireId);
    }

    private void EndAllHunts()
    {
        var now = Now;
        foreach (var hunterId in _hunts.Keys.ToArray()) EndHunt(hunterId, sendHome: true, now, responses: null);
    }

    private static double GroundDistance(DomainPosition from, DomainPosition to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
