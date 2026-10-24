using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private sealed class ScriptMemory
    {
        public DateTimeOffset Started;
        public readonly Dictionary<uint, DateTimeOffset> LastCast = [];
        public readonly Dictionary<uint, int> CastCount = [];
        public readonly HashSet<string> Marks = new(StringComparer.Ordinal);
        public readonly List<string> Summons = [];
    }

    private readonly Dictionary<string, ScriptMemory> _scriptMemory = new(StringComparer.Ordinal);

    private void TickCreatureScripts(DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _shared is null) return;
        var playerId = PlayerUnitId;
        TickDeaths(responses);
        TickCrumblingSummons(now, responses);
        TickCreaturePulses(playerId, now, responses);
        TickCreatureCasts(playerId, now, responses);
        TickPools(now, responses);
        TickRitual(now, responses);
        TickShouts(now, responses);
        if (_combat.IsPlayerDead || _combat.IsDead(playerId)) return;

        foreach (var wireId in _sight.Ids)
        {
            if (BodyGone(wireId)) continue;
            if (!string.Equals(_combat.RetaliationTarget(wireId), playerId, StringComparison.Ordinal)
                && !string.Equals(_combat.CurrentTargetOf(playerId), wireId, StringComparison.Ordinal))
            {
                if (_scriptMemory.Remove(wireId) && _shared.MobVictim(wireId) is null) ResetCreature(wireId, responses);
                _creatureCasts.Remove(wireId);
                continue;
            }

            // One session acts for a creature a whole party is fighting: the one whose player it has chosen.
            if (_shared.MobVictim(wireId) is { } victim && !string.Equals(victim, playerId, StringComparison.Ordinal))
                continue;
            if (_creatureCasts.ContainsKey(wireId)) continue;
            var pulled = !_scriptMemory.ContainsKey(wireId);
            var script = ScriptFor(wireId);
            var action = script.Act(TurnContextFor(wireId, now));
            if (pulled && script.PullLine is { } pullLine) YellAs(wireId, pullLine, responses);
            if (action is null) continue;
            Perform(wireId, action, playerId, now, responses);
        }
    }

    private CombatTurnContext TurnContextFor(string wireId, DateTimeOffset now)
    {
        var memory = MemoryFor(wireId, now);
        var since = new Dictionary<uint, TimeSpan>();
        foreach (var (spellId, at) in memory.LastCast) since[spellId] = now - at;

        return new CombatTurnContext(
            CreatureFor(wireId),
            _combat.HealthFractionOf(wireId),
            now - memory.Started,
            InRangeOf(wireId, (int)MeleeRange),
            since,
            memory.CastCount,
            SummonsInReach(wireId, memory),
            memory.Marks,
            AlliesInReach(wireId),
            _copy.Mode == DungeonMode.Legend,
            MostWoundedAlly(wireId) is not null);
    }

    // Creatures standing through a long cast until the time given: no swings, no chasing.
    private readonly Dictionary<string, DateTimeOffset> _holdingStill = new(StringComparer.Ordinal);

    private bool IsHoldingStill(string wireId, DateTimeOffset at) =>
        _holdingStill.TryGetValue(wireId, out var until) && at < until;

    private void ResetCreature(string wireId, List<string> responses)
    {
        _holdingStill.Remove(wireId);
        _creaturePulses.Clear(wireId);
        _combat.UnshieldUnit(wireId);
        foreach (var spellId in ScriptFor(wireId).ClearedOnReset)
            responses.Add(AuraEnded(wireId, wireId, _copy.Spell(spellId)));
    }

    private void TickShouts(DateTimeOffset now, List<string> responses)
    {
        if (_shared is null) return;
        foreach (var wireId in _sight.Ids)
        {
            var script = ScriptFor(wireId);
            if (script.Shouts.Count == 0 || BodyGone(wireId) || CreatureFor(wireId) is not { } shouter) continue;
            if (!_shared.TryClaimEvery($"shout:{wireId}", script.ShoutEvery, now)) continue;
            var turn = (int)((now - DateTimeOffset.UnixEpoch) / script.ShoutEvery);
            responses.Add(Reply(WebMessageTypes.ChatReceived, new ChatReceivedPayload(
                ChatService.Yell, shouter.NameKey, script.Shouts[turn % script.Shouts.Count], WebNetworkId.FromString(wireId))));
        }
    }

    private ScriptMemory MemoryFor(string wireId, DateTimeOffset now)
    {
        if (_scriptMemory.TryGetValue(wireId, out var existing)) return existing;
        var fresh = new ScriptMemory { Started = now };
        _scriptMemory[wireId] = fresh;
        return fresh;
    }

    private int SummonsInReach(string wireId, ScriptMemory memory)
    {
        if (memory.Summons.Count == 0 || _shared is null) return 0;
        memory.Summons.RemoveAll(id => _combat.IsDead(id) || !_sight.Contains(id));
        if (!_shared.TryGetPosition(wireId, out var bossAt)) return 0;
        return memory.Summons.Count(summonId =>
            _shared.TryGetPosition(summonId, out var summonAt) && Apart(bossAt, summonAt) <= BossTiming.SummonReach);
    }

    private IReadOnlyCollection<uint> AlliesInReach(string wireId)
    {
        if (_shared is null || !_shared.TryGetPosition(wireId, out var at)) return [];
        var allies = new HashSet<uint>();
        foreach (var (otherId, entry) in _sight.Entries)
        {
            if (string.Equals(otherId, wireId, StringComparison.Ordinal) || BodyGone(otherId)) continue;
            if (_shared.TryGetPosition(otherId, out var otherAt) && Apart(at, otherAt) <= BossTiming.SummonReach)
                allies.Add(_copy.AuthoredEntry(entry));
        }

        return allies;
    }

    private static double Apart(DomainPosition a, DomainPosition b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private void Perform(
        string wireId, CombatAction action, string playerId, DateTimeOffset now, List<string> responses)
    {
        var memory = MemoryFor(wireId, now);
        if (action.Clears is not null) memory.Marks.Remove(action.Clears);
        if (action.Sets is not null) memory.Marks.Add(action.Sets);

        if (action.Yell is { } yell) YellAs(wireId, yell, responses);
        if (action.Ends is uint ended) EndCreatureAura(wireId, _copy.Spell(ended), responses);
        if (action.Summon is uint entryId) SummonForFight(wireId, entryId, action.SummonLasts, playerId, memory, now, responses);
        if (action.Cast is not uint spellId) return;
        var targetId = CastAsCreature(wireId, spellId, action.At, action.AllyEntry, playerId, memory, now, responses);
        if (action.HoldsStill && _creatureCasts.TryGetValue(wireId, out var holding)) _holdingStill[wireId] = holding.LandsAt;
        if (action.LeavesPool is uint pool && targetId is not null
            && _content.Spells.TryGetValue(_copy.Spell(spellId), out var cast))
            DropPool(wireId, _copy.Spell(pool), targetId, TimeSpan.FromMilliseconds(cast.DurationMs), now);
    }

    private string? CastAsCreature(
        string wireId, uint spellId, CastTarget at, uint? allyEntry, string playerId,
        ScriptMemory memory, DateTimeOffset now, List<string> responses)
    {
        // Counted even when the row is missing, so a script never stalls retrying a spell it cannot cast.
        memory.LastCast[spellId] = now;
        memory.CastCount[spellId] = memory.CastCount.TryGetValue(spellId, out var count) ? count + 1 : 1;

        var castId = _copy.Spell(spellId);
        if (!_content.Spells.TryGetValue(castId, out var spell)) return null;

        var targetId = at switch
        {
            CastTarget.Self => wireId,
            CastTarget.OwnSummon => memory.Summons.FirstOrDefault() ?? wireId,
            CastTarget.Ally => NearestAlly(wireId, allyEntry) ?? wireId,
            CastTarget.WoundedAlly => MostWoundedAlly(wireId) ?? wireId,
            CastTarget.AnyEnemy => AnyEnemyOf(wireId, playerId),
            _ => playerId,
        };

        if (spell.CastTimeMs > 0)
        {
            _creatureCasts[wireId] = new PendingCreatureCast(castId, spell, targetId, now.AddMilliseconds(spell.CastTimeMs));
            responses.Add(Reply(WebMessageTypes.SpellCasting,
                new SpellCastingPayload(castId, WebNetworkId.FromString(wireId), spell.CastTimeMs)));
            return targetId;
        }

        LandCreatureSpell(wireId, targetId, castId, spell, now, responses);
        return targetId;
    }

    private void YellAs(string wireId, string line, List<string> responses)
    {
        if (CreatureFor(wireId) is not { } shouter) return;
        responses.Add(Reply(WebMessageTypes.ChatReceived,
            new ChatReceivedPayload(ChatService.Yell, shouter.NameKey, line, WebNetworkId.FromString(wireId))));
    }

    // Own choice: "under half health", the guide's line for when a healer steps in.
    private const double WoundedBelow = 0.5;

    private string? MostWoundedAlly(string wireId)
    {
        if (_shared is null || !_shared.TryGetPosition(wireId, out var at)) return null;
        string? worst = _combat.HealthFractionOf(wireId) <= WoundedBelow ? wireId : null;
        var worstFraction = worst is null ? WoundedBelow : _combat.HealthFractionOf(wireId);
        foreach (var otherId in _sight.Ids)
        {
            if (string.Equals(otherId, wireId, StringComparison.Ordinal) || BodyGone(otherId)) continue;
            if (!_shared.TryGetPosition(otherId, out var otherAt) || Apart(at, otherAt) > BossTiming.SummonReach) continue;
            if (FactionOf(CreatureFor(otherId)) != FactionOf(CreatureFor(wireId))) continue;
            var fraction = _combat.HealthFractionOf(otherId);
            if (fraction > worstFraction) continue;
            worst = otherId;
            worstFraction = fraction;
        }

        return worst;
    }

    private string? NearestAlly(string wireId, uint? allyEntry)
    {
        if (_shared is null || allyEntry is not uint wanted || !_shared.TryGetPosition(wireId, out var at)) return null;
        return _sight.Entries
            .Where(pair => !string.Equals(pair.Key, wireId, StringComparison.Ordinal)
                && _copy.AuthoredEntry(pair.Value) == wanted && !BodyGone(pair.Key))
            .Select(pair => (Id: pair.Key, Distance: _shared.TryGetPosition(pair.Key, out var p) ? Apart(at, p) : double.MaxValue))
            .Where(ally => ally.Distance <= BossTiming.SummonReach)
            .OrderBy(ally => ally.Distance)
            .Select(ally => ally.Id)
            .FirstOrDefault();
    }

    private void SummonForFight(
        string wireId, uint entryId, TimeSpan? lasts, string playerId, ScriptMemory memory, DateTimeOffset now,
        List<string> responses)
    {
        if (_shared is null || !_shared.TryGetPosition(wireId, out var bossAt)) return;

        var key = BossTiming.SummonKey(entryId);
        memory.LastCast[key] = now;
        memory.CastCount[key] = memory.CastCount.TryGetValue(key, out var count) ? count + 1 : 1;

        var angle = memory.Summons.Count * 1.7;
        var at = new DomainPosition(
            (float)(bossAt.X + (Math.Cos(angle) * 4)), bossAt.Y, (float)(bossAt.Z + (Math.Sin(angle) * 4)));
        if (SummonAt(entryId, at, responses) is not { } summonId) return;
        memory.Summons.Add(summonId);
        if (lasts is TimeSpan life) _summonCrumblesAt[summonId] = now + life;

        // An add called into a fight fights from the moment it is there: its own notice range and faction
        // would leave it standing by (a puppet has neither) until someone hit it.
        _combat.EnlistAttacker(summonId, playerId, now);
        NotePackJoiner(summonId);
        HoldEngaged(summonId);
        _shared.ClearReturning(summonId);
        EmitJoinResponses(responses, summonId, playerId);
    }

    private string? SummonAt(uint authoredEntryId, DomainPosition at, List<string> responses)
    {
        if (_instanceWorld is null || _shared is null) return null;
        var spawn = new CreatureSpawnDefinition(
            NextSummonSpawnId(), _instanceWorld.MapId, _copy.CreatureEntry(authoredEntryId), at.X, at.Y, at.Z, 0);

        if (_instanceWorld.Summon(spawn) is not { } entity) return null;
        _spawnById[spawn.Id] = spawn;
        var summonId = entity.Id.ToString();
        var home = new DomainPosition(entity.Position.X, entity.Position.Y, entity.Position.Z);
        _shared.Adopt(summonId, entity.SpawnId, home);
        _shared.SetPosition(summonId, home);
        RegisterCreature(entity);
        responses.Add(SpawnedReply(entity));
        return summonId;
    }

    private uint NextSummonSpawnId() => FightSummonSpawnIdBase + _nextFightSummon++;

    private uint _nextFightSummon;

    private const uint FightSummonSpawnIdBase = 1_500_000_000;

    private void AnnounceScriptedKill(string killerId, string targetId, string playerId, List<string> responses)
    {
        if (!string.Equals(targetId, playerId, StringComparison.Ordinal)) return;
        var killer = WebNetworkId.FromString(killerId);
        var victim = WebNetworkId.FromString(targetId);
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(victim)));
        responses.AddRange(ApplyDeathPenalty(killerId));
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(killer)));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(victim)));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(killer)));
        ReleaseEngaged(killerId);
        UnmarkEngaged();
        _combat.Disengage(playerId);
    }
}
