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
        if (_combat.IsPlayerDead || _combat.IsDead(playerId)) return;

        foreach (var wireId in _sight.Ids)
        {
            if (BodyGone(wireId)) continue;
            if (!string.Equals(_combat.RetaliationTarget(wireId), playerId, StringComparison.Ordinal)
                && !string.Equals(_combat.CurrentTargetOf(playerId), wireId, StringComparison.Ordinal))
            {
                _scriptMemory.Remove(wireId);
                continue;
            }

            var action = ScriptFor(wireId).Act(TurnContextFor(wireId, now));
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
            memory.Marks);
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

        var count = 0;
        foreach (var summonId in memory.Summons)
        {
            if (!_shared.TryGetPosition(summonId, out var summonAt)) continue;
            var dx = summonAt.X - bossAt.X;
            var dz = summonAt.Z - bossAt.Z;
            if (Math.Sqrt((dx * dx) + (dz * dz)) <= BossTiming.SummonReach) count++;
        }

        return count;
    }

    private void Perform(
        string wireId, CombatAction action, string playerId, DateTimeOffset now, List<string> responses)
    {
        var memory = MemoryFor(wireId, now);
        if (action.Clears is not null) memory.Marks.Remove(action.Clears);
        if (action.Sets is not null) memory.Marks.Add(action.Sets);

        if (action.Yell is { } yell && CreatureFor(wireId) is { } shouter)
            responses.Add(Reply(WebMessageTypes.ChatReceived, new ChatReceivedPayload(ChatService.Yell, shouter.NameKey, yell)));
        if (action.Summon is uint entryId) SummonForFight(wireId, entryId, memory, now, responses);
        if (action.Cast is uint spellId) CastAsCreature(wireId, spellId, action.At, playerId, memory, now, responses);
    }

    private void CastAsCreature(
        string wireId, uint spellId, CastTarget at, string playerId,
        ScriptMemory memory, DateTimeOffset now, List<string> responses)
    {
        var castId = _copy.Spell(spellId);
        if (!_content.Spells.TryGetValue(castId, out var spell)) return;

        memory.LastCast[spellId] = now;
        memory.CastCount[spellId] = memory.CastCount.TryGetValue(spellId, out var count) ? count + 1 : 1;

        var caster = WebNetworkId.FromString(wireId);
        var targetId = at switch
        {
            CastTarget.Self => wireId,
            CastTarget.OwnSummon => memory.Summons.FirstOrDefault() ?? wireId,
            _ => playerId,
        };
        var target = WebNetworkId.FromString(targetId);
        responses.Add(Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(castId, caster, [target], [], spell.Speed, Aimed: false)));

        if (spell.EffectHeal is int heal)
        {
            var restored = _combat.RestoreHealth(targetId, heal);
            if (restored.Applied > 0)
                MarkUnitValues(targetId, new Dictionary<string, int>
                {
                    [WireCodes.UnitValue.Health] = restored.FinalHealth,
                    [WireCodes.UnitValue.MaxHealth] = restored.MaxHealth,
                }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome,
                new SpellOutcomePayload(
                    caster, target, castId, WireCodes.Outcome.Hit, restored.Applied, restored.FinalHealth)));
            return;
        }

        if (spell.EffectDamage is int baseDamage)
        {
            var damage = spell.EffectDamageRandom is int spread
                ? _combat.RollAmount(baseDamage, baseDamage + spread)
                : baseDamage;
            if (string.Equals(targetId, playerId, StringComparison.Ordinal))
            {
                damage = CombatService.ApplyArmorMitigation(
                    damage, _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Armor, now));
            }

            var swing = _combat.DamageTarget(
                wireId, targetId, damage, now,
                _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now));
            MarkUnitValues(targetId, new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Health] = swing.FinalHealth,
                [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth,
            }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome,
                new SpellOutcomePayload(
                    caster, target, castId, WireCodes.Outcome.Hit, swing.Damage, swing.FinalHealth, WireAbsorbed(swing))));
            if (swing.Killed) AnnounceScriptedKill(wireId, targetId, playerId, responses);
            return;
        }

        int durationMs = checked((int)AuraService.Duration.TotalMilliseconds);
        responses.Add(Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(
            castId, target, caster,
            Harmful: string.Equals(targetId, playerId, StringComparison.Ordinal) ? 1 : 0,
            MaxDuration: durationMs, Remaining: durationMs)));
    }

    private void SummonForFight(
        string wireId, uint entryId, ScriptMemory memory, DateTimeOffset now, List<string> responses)
    {
        if (_instanceWorld is null || _shared is null) return;
        if (!_shared.TryGetPosition(wireId, out var bossAt)) return;

        var key = BossTiming.SummonKey(entryId);
        memory.LastCast[key] = now;
        memory.CastCount[key] = memory.CastCount.TryGetValue(key, out var count) ? count + 1 : 1;

        var angle = memory.Summons.Count * 1.7;
        var spawn = new CreatureSpawnDefinition(
            NextSummonSpawnId(), _instanceWorld.MapId, _copy.CreatureEntry(entryId),
            (float)(bossAt.X + (Math.Cos(angle) * 4)),
            bossAt.Y,
            (float)(bossAt.Z + (Math.Sin(angle) * 4)),
            0);

        if (_instanceWorld.Summon(spawn) is not { } entity) return;
        _spawnById[spawn.Id] = spawn;
        var summonId = entity.Id.ToString();
        var home = new DomainPosition(entity.Position.X, entity.Position.Y, entity.Position.Z);
        _shared.Adopt(summonId, entity.SpawnId, home);
        _shared.SetPosition(summonId, home);
        RegisterCreature(entity);
        memory.Summons.Add(summonId);
        responses.Add(SpawnedReply(entity));
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
