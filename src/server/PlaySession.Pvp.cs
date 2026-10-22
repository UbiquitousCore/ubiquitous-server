using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly PvpFlagService _pvpFlags;

    private IReadOnlyList<string> PvpFlagSet(JsonElement payload)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Character selection is required before flagging for PvP.")];
        var playerId = PlayerUnitId;
        var flagged = ReadFlagged(payload);
        var result = _pvpFlags.SetFlagged(playerId, flagged);
        return [Reply(WebMessageTypes.PvpFlagChanged, new PvpFlagChangedPayload(result))];
    }

    private readonly PlayerBlowRelay _blows;

    private bool IsPlayerOpponent(string targetId)
    {
        if (_character is null || !targetId.StartsWith("player:", StringComparison.Ordinal)) return false;
        var playerId = PlayerUnitId;
        return _duels.IsActiveOpponent(playerId, targetId) || _pvpFlags.IsFlaggedPair(playerId, targetId);
    }

    private PlaySession? PlayerAlongside(string targetId)
    {
        if (_character is null || !targetId.StartsWith("player:", StringComparison.Ordinal)
            || targetId == PlayerUnitId) return null;
        var other = _hub?.FindByCharacterNetworkId(WebNetworkId.FromString(targetId));
        return other is not null && other.CurrentMapId == CurrentMapId && other.CurrentInstanceKey == CurrentInstanceKey
            && !other.HidesFrom(this)
            ? other : null;
    }

    private bool IsFriendlyPlayer(string targetId) => PlayerAlongside(targetId) is not null && !IsPlayerOpponent(targetId);

    private void HandOverBlow(CombatSwing swing, PlayerBlowKind kind, uint spellId = 0)
    {
        if (HiddenFromMe(swing.TargetId)) return;
        _blows.Send(new PlayerBlow(swing.AttackerId, swing.TargetId, swing.Damage, swing.Result, kind, swing.OffHand, spellId));
    }

    private void TakeIncomingBlows(DateTimeOffset now, int armor, int stamina, List<string> responses)
    {
        if (_character is null) return;
        var playerId = PlayerUnitId;
        var self = WebNetworkId.FromString(playerId);
        foreach (var blow in _blows.Drain(playerId))
        {
            if (_combat.IsPlayerDead) continue;
            if (blow.Kind == PlayerBlowKind.CreatureSpell)
            {
                TakeRelayedCreatureSpell(blow, now, responses);
                continue;
            }
            if (blow.Kind is PlayerBlowKind.Heal or PlayerBlowKind.Aura)
            {
                if (IsPlayerOpponent(blow.AttackerId)) continue;
                if (blow.Kind == PlayerBlowKind.Heal) TakeHeal(blow, self, stamina, now, responses);
                else TakeLastingEffect(blow, now, responses);
                continue;
            }
            if (!IsPlayerOpponent(blow.AttackerId)) continue;
            if (blow.Kind == PlayerBlowKind.Harm)
            {
                TakeLastingEffect(blow, now, responses);
                continue;
            }
            var damage = blow.Kind == PlayerBlowKind.Spell ? blow.Amount : CombatService.ApplyArmorMitigation(blow.Amount, armor);
            var swing = _combat.DamageTarget(blow.AttackerId, playerId, damage, now, stamina);
            var attacker = WebNetworkId.FromString(blow.AttackerId);
            var outcome = WireOutcome(blow.Result);
            responses.Add(blow.Kind switch
            {
                PlayerBlowKind.Shot => Reply(WebMessageTypes.AttackShotResult, new AttackShotResultPayload(
                    attacker, self, outcome, swing.Damage, swing.FinalHealth, WireAbsorbed(swing))),
                PlayerBlowKind.Spell => Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                    attacker, self, blow.SpellId, outcome, swing.Damage, swing.FinalHealth, WireAbsorbed(swing))),
                _ => Reply(WebMessageTypes.AttackSwingResult, new AttackSwingResultPayload(
                    attacker, self, outcome, swing.Damage, swing.FinalHealth,
                    blow.OffHand ? WireCodes.Hand.Off : WireCodes.Hand.Main, WireAbsorbed(swing))),
            });
            MarkUnitValues(playerId, new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Health] = swing.FinalHealth,
                [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth,
            }, now);
            if (swing.Spared) responses.AddRange(EndDuelAsLoser(playerId));
            else if (swing.Killed) DieTo(blow.AttackerId, responses);
        }
    }

    private void TakeHeal(PlayerBlow heal, WebNetworkId self, int stamina, DateTimeOffset now, List<string> responses)
    {
        var healed = _combat.HealPlayer(heal.Amount, stamina);
        if (healed is null) return;
        responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
            WebNetworkId.FromString(heal.AttackerId), self, heal.SpellId, WireCodes.Outcome.Heal,
            healed.Applied, healed.FinalHealth)));
        MarkUnitValues(self.Text!, new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Health] = healed.FinalHealth,
            [WireCodes.UnitValue.MaxHealth] = healed.MaxHealth,
        }, now);
    }

    private void TakeLastingEffect(PlayerBlow effect, DateTimeOffset now, List<string> responses)
    {
        if (_character is null || !_content.Spells.TryGetValue(effect.SpellId, out var cast)) return;
        var spell = effect.ExtraEffect == PlayerBlow.MainEffect ? cast
            : cast.ExtraEffects?.ElementAtOrDefault(effect.ExtraEffect);
        if (spell is null) return;
        var caster = WebNetworkId.FromString(effect.AttackerId);
        var self = PlayerUnitId;
        if (ResolvesAsDamage(spell))
        {
            ApplyDamageRiders(caster, self, effect.SpellId, spell, now, atOtherPlayer: false, responses);
            return;
        }
        responses.AddRange(ResolvePrimarySpellEffect(caster, self, effect.SpellId, spell, now).Where(message =>
        {
            var type = WebMessageJson.Deserialize(message).Type;
            return type != WebMessageTypes.SpellReleased && type != WebMessageTypes.SpellCooldownStarted;
        }));
    }

    private void TakeDamageOverTimeTick(AuraTick tick, DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _combat.IsPlayerDead || tick.CasterId is null) return;
        var playerId = PlayerUnitId;
        var stamina = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
        var swing = _combat.DamageTarget(tick.CasterId, playerId, tick.PerTick, now, stamina);
        responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
            WebNetworkId.FromString(tick.CasterId), WebNetworkId.FromString(playerId), tick.SpellId,
            WireCodes.Outcome.Hit, swing.Damage, swing.FinalHealth, WireAbsorbed(swing))));
        MarkUnitValues(playerId, new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Health] = swing.FinalHealth,
            [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth,
        }, now);
        if (swing.Spared) responses.AddRange(EndDuelAsLoser(playerId));
        else if (swing.Killed) DieTo(tick.CasterId, responses);
    }

    private void EndEffectsOnSelf(Func<string?, bool, bool>? match, DateTimeOffset now, List<string> responses)
    {
        if (_character is null) return;
        var self = PlayerUnitId;
        bool On(string target) => string.Equals(target, self, StringComparison.Ordinal);
        bool everything = match is null;
        match ??= static (_, _) => true;
        var ended = new List<(string? Caster, uint SpellId)>();
        ended.AddRange(_auras.ClearWhere(a => On(a.TargetId) && match(a.CasterId, !a.IsHeal)).Select(a => (a.CasterId, a.SpellId)));
        ended.AddRange(_control.ClearWhere(e => On(e.TargetId) && match(e.CasterId, true)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_silence.ClearWhere(e => On(e.TargetId) && match(e.CasterId, true)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_slow.ClearWhere(e => On(e.TargetId) && match(e.CasterId, true)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_speedup.ClearWhere(e => On(e.TargetId) && match(e.CasterId, e.Percent < 0)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_meleeAttackSpeed.ClearWhere(e => On(e.TargetId) && match(e.CasterId, e.Percent < 0)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_chill.ClearWhere(e => On(e.TargetId) && match(e.CasterId, true)).Select(e => (e.CasterId, e.SpellId)));
        ended.AddRange(_attributeModifiers.ClearWhere(m => On(m.TargetId) && match(m.CasterId, m.Amount < 0)).Select(m => (m.CasterId, m.SpellId)));
        if (everything)
        {
            Dismount(now, responses);
            ended.AddRange(_absorb.Clear(self).Select(spellId => ((string?)null, spellId)));
            ended.AddRange(_stealth.Clear(self).Select(spellId => ((string?)null, spellId)));
            ended.AddRange(_periodicTrigger.Clear(self).Select(spellId => ((string?)null, spellId)));
        }
        foreach (var (caster, spellId) in ended.Distinct()) responses.Add(AuraEnded(caster, self, spellId));
        MarkAuraStateUpdates(now);
    }

    private void EndHarmFrom(string opponentId, DateTimeOffset now, List<string> responses) =>
        EndEffectsOnSelf((caster, harmful) => harmful && string.Equals(caster, opponentId, StringComparison.Ordinal),
            now, responses);

    private static bool ReadFlagged(JsonElement payload) =>
        !payload.TryGetProperty("flagged", out var value) || value.ValueKind != JsonValueKind.False;
}
