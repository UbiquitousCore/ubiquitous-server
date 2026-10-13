using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    internal static string WireOutcome(StrikeResult result) => result switch
    {
        StrikeResult.Miss => WireCodes.Outcome.Miss,
        StrikeResult.Dodge => WireCodes.Outcome.Dodge,
        StrikeResult.Parry => WireCodes.Outcome.Parry,
        StrikeResult.Block => WireCodes.Outcome.Block,
        StrikeResult.Critical => WireCodes.Outcome.Critical,
        StrikeResult.Evade => WireCodes.Outcome.Evade,
        _ => WireCodes.Outcome.Hit,
    };

    internal static int? WireAbsorbed(CombatSwing swing) => swing.Absorbed > 0 ? swing.Absorbed : null;

    private IReadOnlyList<string> SpellRequest(JsonElement payload, DateTimeOffset now)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.InvalidState, "Character selection is required before casting.")];
        if (!payload.TryGetProperty("spellId", out var spellIdValue) || !spellIdValue.TryGetUInt32(out var spellId))
            return [Error(WireCodes.ProtocolError.InvalidPayload, "A spell id is required.")];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        if (_gathering.InProgress)
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.AlreadyCasting))];
        if (_control.IsControlled(PlayerUnitId, now))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.Incapacitated))];
        if (_silence.IsControlled(PlayerUnitId, now))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.Silenced))];

        if (OperatorSpells.All.Contains(spellId) && _account?.IsAdmin != true)
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.InvalidTarget))];

        _content.Spells.TryGetValue(spellId, out var peekedSpell);
        if (_riding is not null && MountRules.IsMount(peekedSpell) && _spells.Known.Contains(spellId))
            return MountDismount(now);
        if (peekedSpell is not null && !MeetsEquipRequirement(peekedSpell))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.MissingEquipment))];
        bool isSelfHeal = peekedSpell is not null && (peekedSpell.EffectHeal is not null || peekedSpell.AuraPeriodicHeal is not null);
        bool isSelfBuff = peekedSpell is not null && peekedSpell.AuraAttributeId is not null
            && (peekedSpell.AuraAttributeAmount ?? 0) >= 0;
        bool isSelfStealth = peekedSpell is not null && peekedSpell.AuraStealth;
        bool isSelfShield = peekedSpell?.AuraAbsorbAmount is not null;
        bool namesCaster = peekedSpell?.EffectTarget == SpellEffectTargets.Self;
        var namedTargetId = PayloadIds.Read(payload, "targetId");
        bool atFriend = !namesCaster && LandsOnFriend(peekedSpell) && !string.IsNullOrEmpty(namedTargetId)
            && IsFriendlyPlayer(namedTargetId);
        bool isSelfTargeted = namesCaster || (!atFriend && (isSelfHeal || isSelfBuff || isSelfStealth || isSelfShield));
        var targetId = isSelfTargeted ? PlayerUnitId : namedTargetId;

        bool atPlayerOpponent = !isSelfTargeted && !string.IsNullOrEmpty(targetId)
            && IsPlayerOpponent(targetId) && !HiddenFromMe(targetId) && AimsAtOpponent(peekedSpell);
        if (!isSelfTargeted && (string.IsNullOrEmpty(targetId)
            || (!_sight.Contains(targetId) && !atPlayerOpponent && !atFriend) || _combat.IsDead(targetId)))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.InvalidTarget))];
        if (!isSelfTargeted && WouldHarmCreature(peekedSpell) && IsFriendlyCreature(targetId!))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.InvalidTarget))];
        if (!isSelfTargeted && peekedSpell is not null && !InRangeOf(targetId!, CastRangeOf(peekedSpell)))
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, WireCodes.SpellRefusal.OutOfRange))];

        var wisdomBonus = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.Wisdom, now);
        var started = _spells.TryBeginCast(spellId, _content.Spells, targetId, now, out var refusal, wisdomBonus);
        if (started is null)
        {
            var code = refusal!.Error switch
            {
                SpellRefusalReason.UnknownSpell => WireCodes.SpellRefusal.UnknownSpell,
                SpellRefusalReason.NoEffectData => WireCodes.SpellRefusal.NoEffectData,
                SpellRefusalReason.OnCooldown => WireCodes.SpellRefusal.OnCooldown,
                SpellRefusalReason.InsufficientPower => WireCodes.SpellRefusal.NotEnoughPower,
                SpellRefusalReason.AlreadyCasting => WireCodes.SpellRefusal.AlreadyCasting,
                _ => WireCodes.SpellRefusal.UnknownSpell,
            };
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, spellId, code))];
        }

        var responses = new List<string>();
        Dismount(now, responses);
        if (!isSelfTargeted && !atFriend) BreakStealth(now, responses);
        if (started.IsInstant)
        {
            MarkUnitValues(casterId.Text!, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = _spells.Power }, now);
        }

        if (!started.IsInstant)
        {
            responses.Add(Reply(WebMessageTypes.SpellCasting, new SpellCastingPayload(spellId, casterId, started.Spell.CastTimeMs)));
            return responses;
        }

        responses.AddRange(RecordQuestSpellCast(spellId, targetId));
        responses.AddRange(ReleaseSpell(casterId, targetId!, spellId, started.Spell, now));
        MarkAuraStateUpdates(now, targetId);
        return responses;
    }

    private IReadOnlyList<string> LandFinishedCast(SpellCastReady finished, DateTimeOffset now)
    {
        var playerId = PlayerUnitId;
        var casterId = WebNetworkId.FromString(playerId);
        if (FinishedCastTarget(finished, out var refusal) is not { } targetId)
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(casterId, finished.SpellId, refusal))];

        _spells.Spend(finished, now, _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now));
        MarkUnitValues(playerId, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = _spells.Power }, now);
        var responses = new List<string>(RecordQuestSpellCast(finished.SpellId, finished.TargetId));
        responses.AddRange(ReleaseSpell(casterId, targetId, finished.SpellId, finished.Spell, now));
        return responses;
    }

    private IReadOnlyList<string> ReleaseSpell(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        if (targetId == casterId.Text || !SpellFlightService.Flies(spell) || DistanceTo(targetId) is not { } distance)
            return ResolveSpellEffect(casterId, targetId, spellId, spell, now);
        _spellFlights.Launch(spellId, spell, targetId, distance, now);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(
                spellId, casterId, [WebNetworkId.FromString(targetId)], [], spell.Speed, Aimed: false)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private void LandSpellsInFlight(DateTimeOffset now, List<string> responses)
    {
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        foreach (var arrived in _spellFlights.TakeArrived(now))
        {
            if (!StillThere(arrived.TargetId)) continue;
            responses.AddRange(ResolveSpellEffect(casterId, arrived.TargetId, arrived.SpellId, arrived.Spell, now)
                .Where(message => !AnnouncedAtLaunch(message, arrived.SpellId)));
            MarkAuraStateUpdates(now, arrived.TargetId);
        }
    }

    private bool StillThere(string targetId) =>
        !_combat.IsDead(targetId)
        && (_sight.Contains(targetId) || IsFriendlyPlayer(targetId)
            || (IsPlayerOpponent(targetId) && !HiddenFromMe(targetId)));

    private static bool AnnouncedAtLaunch(string message, uint spellId)
    {
        var envelope = WebMessageJson.Deserialize(message);
        return envelope.Type is WebMessageTypes.SpellReleased or WebMessageTypes.SpellCooldownStarted
            && envelope.Payload.TryGetProperty("spellId", out var id) && id.TryGetUInt32(out var announced)
            && announced == spellId;
    }

    private string? FinishedCastTarget(SpellCastReady finished, out string refusal)
    {
        refusal = WireCodes.SpellRefusal.InvalidTarget;
        if (finished.TargetId == PlayerUnitId) return PlayerUnitId;
        if (finished.TargetId is not { } targetId || _combat.IsDead(targetId)) return null;
        if (WouldHarmCreature(finished.Spell) && IsFriendlyCreature(targetId)) return null;
        bool atFriend = LandsOnFriend(finished.Spell) && IsFriendlyPlayer(targetId);
        bool atFoe = _sight.Contains(targetId)
            || (IsPlayerOpponent(targetId) && !HiddenFromMe(targetId) && HarmsPlayer(finished.Spell));
        if (!atFriend && !atFoe) return null;
        if (InRangeOf(targetId, CastRangeOf(finished.Spell))) return targetId;
        refusal = WireCodes.SpellRefusal.OutOfRange;
        return null;
    }

    private IReadOnlyList<string> RecordQuestSpellCast(uint spellId, string? targetId) =>
        _quests.RecordSpellCast(spellId, targetId, _content.Quests)
            .Select(update => Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)))
            .ToList();

    private const int MaxEffectDepth = 4;
    private int _effectDepth;

    private IReadOnlyList<string> ResolveSpellEffect(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        if (targetId != casterId.Text && IsReturningHome(targetId)) return EvadedCast(casterId, targetId, spellId, spell);
        if (targetId != casterId.Text && IsFriendlyCreature(targetId) && WouldHarmCreature(spell)) return [];
        var responses = new List<string>();
        if (spell.EffectLeapToTarget && targetId != casterId.Text) responses.AddRange(LeapToward(targetId));
        if (_effectDepth >= MaxEffectDepth) return responses;
        _effectDepth++;
        try
        {
            var primary = ResolvePrimarySpellEffect(casterId, targetId, spellId, spell, now);
            AppendCombatEntries(responses);
            responses.AddRange(primary);
            ResolveCasterExtras(casterId, targetId, spellId, spell, now, responses);
            if (!ResolvesAsDamage(spell))
                ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed: true, responses);
            ResolveTrigger(casterId, targetId, spell.AuraTriggerSpellId, spell.AuraTriggerChancePercent, now, responses);
            ResolveTrigger(casterId, targetId, spell.EffectTriggerSpellId, spell.EffectTriggerChancePercent, now, responses);
        }
        finally
        {
            _effectDepth--;
        }

        return responses;
    }

    private void ResolveTrigger(
        WebNetworkId casterId, string targetId, uint? triggeredId, int? chancePercent, DateTimeOffset now, List<string> responses)
    {
        if (triggeredId is not uint id
            || !_content.Spells.TryGetValue(id, out var triggered)
            || !SpellService.HasResolvableEffect(triggered)
            || !_spells.RollTriggerChance(chancePercent ?? 0))
            return;
        var at = triggered.EffectTarget == SpellEffectTargets.Self ? casterId.Text! : targetId;
        if (IsPlayerOpponent(at) && !HarmsPlayer(triggered)) return;
        responses.AddRange(ResolveSpellEffect(casterId, at, id, triggered, now));
    }

    private bool LandsOnCaster(SpellDefinition extra, string targetId, WebNetworkId casterId) =>
        extra.EffectTarget == SpellEffectTargets.Self
        || (SpellEffectTargets.IsFriendly(extra.EffectTarget)
            && (targetId == casterId.Text || !IsFriendlyPlayer(targetId)));

    private static bool IsStrike(SpellDefinition spell) =>
        spell.EffectDamage is not null || spell.EffectMeleeWeaponHit || spell.EffectRangedWeaponHit;

    private void ResolveCasterExtras(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now, List<string> responses)
    {
        if (spell.ExtraEffects is null) return;
        foreach (var extra in spell.ExtraEffects)
        {
            if (!LandsOnCaster(extra, targetId, casterId) || !SpellService.HasResolvableEffect(extra)) continue;
            responses.AddRange(WithoutRelease(ResolveSpellEffect(casterId, casterId.Text!, spellId, extra, now)));
        }
    }

    private int _resolvingExtra = PlayerBlow.MainEffect;

    private void ResolveTargetExtras(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now, bool landed,
        List<string> responses)
    {
        if (spell.ExtraEffects is null) return;
        foreach (var (extra, index) in spell.ExtraEffects.Select((extra, index) => (extra, index)))
        {
            if (LandsOnCaster(extra, targetId, casterId) || !SpellService.HasResolvableEffect(extra)) continue;
            if (!landed && !IsStrike(extra)) continue;
            if (targetId == casterId.Text || _combat.IsDead(targetId)) continue;
            if (!_sight.Contains(targetId)
                && !(IsPlayerOpponent(targetId) && (HarmsPlayer(extra) || extra.EffectLeapToTarget))) continue;
            var outer = _resolvingExtra;
            _resolvingExtra = index;
            try
            {
                responses.AddRange(WithoutRelease(ResolveSpellEffect(casterId, targetId, spellId, extra, now)));
            }
            finally
            {
                _resolvingExtra = outer;
            }
        }
    }

    private static IEnumerable<string> WithoutRelease(IEnumerable<string> messages) =>
        messages.Where(message => !WebMessageJson.Deserialize(message).Type.Equals(
            WebMessageTypes.SpellReleased, StringComparison.Ordinal));

    private void LandSpellShot(WebNetworkId casterId, CombatSwing swing, SpellDefinition spell, DateTimeOffset now, List<string> responses)
    {
        bool landed = swing.Result is not (StrikeResult.Miss or StrikeResult.Dodge or StrikeResult.Parry);
        if (landed) ApplyDamageRiders(casterId, swing.TargetId, swing.SpellId, spell, now, atOtherPlayer: false, responses);
        ResolveTargetExtras(casterId, swing.TargetId, swing.SpellId, spell, now, landed, responses);
    }

    private static bool ResolvesAsDamage(SpellDefinition? spell) =>
        spell is not null
        && spell.EffectHeal is null
        && spell.AuraPeriodicDamage is null && spell.AuraPeriodicHeal is null
        && spell.AuraPeriodicTriggerSpellId is null
        && !spell.AuraStun && !spell.AuraSleep && !spell.AuraSilence
        && (spell.AuraSlowPercent is null || spell.EffectDamage is not null) && spell.AuraSpeedupPercent is null
        && spell.AuraMeleeAttackSpeedPercent is null
        && spell.AuraRangeAttackSpeedPercent is null && spell.EffectPowerRestore is null
        && !spell.AuraStealth && spell.AuraAttributeId is null && spell.AuraAbsorbAmount is null
        && IsStrike(spell);

    private static bool LandsOnFriend(SpellDefinition? spell)
    {
        if (spell is null) return false;
        if (spell.EffectHeal is not null) return true;
        if (spell.AuraPeriodicDamage is not null || spell.AuraPeriodicHeal is not null) return spell.AuraPeriodicHeal is not null;
        if (spell.AuraPeriodicTriggerSpellId is not null || spell.AuraStun || spell.AuraSleep || spell.AuraSilence
            || spell.AuraSlowPercent is not null) return false;
        if (spell.AuraSpeedupPercent is not null) return spell.AuraSpeedupPercent > 0;
        if (spell.AuraMeleeAttackSpeedPercent is not null || spell.AuraRangeAttackSpeedPercent is not null
            || spell.AuraChill || spell.AuraStealth) return false;
        return spell.AuraAttributeId is not null && (spell.AuraAttributeAmount ?? 0) >= 0;
    }

    private static bool LandsOnOpponent(SpellDefinition? spell)
    {
        if (spell is null || spell.EffectHeal is not null) return false;
        if (spell.AuraPeriodicDamage is not null || spell.AuraPeriodicHeal is not null) return spell.AuraPeriodicHeal is null;
        if (spell.AuraPeriodicTriggerSpellId is not null) return false;
        if (spell.AuraStun || spell.AuraSleep || spell.AuraSilence) return true;
        if (spell.AuraSlowPercent is not null && spell.EffectDamage is null) return true;
        if (spell.AuraSpeedupPercent is not null) return spell.AuraSpeedupPercent < 0;
        if (spell.AuraMeleeAttackSpeedPercent is not null) return spell.AuraMeleeAttackSpeedPercent < 0;
        if (spell.AuraChill && !IsStrike(spell)) return true;
        if (spell.AuraStealth || spell.AuraAbsorbAmount is not null) return false;
        return spell.AuraAttributeId is not null && (spell.AuraAttributeAmount ?? 0) < 0;
    }

    private const float LeapStandOff = 1.5f;

    private IReadOnlyList<string> LeapToward(string targetId)
    {
        if (_world is null || !TryPositionOf(targetId, out var target)) return [];
        var from = _world.PlayerPosition;
        var landing = LeapRules.Landing(from, target, LeapStandOff);
        if (landing is not { } at) return [];
        var yaw = (float)FacingYaw(at, target);
        _world.ApplyPlayerMovement(at, yaw, bypassValidation: true);
        var wire = new Position(at.X, at.Y, at.Z);
        return
        [
            Reply(WebMessageTypes.MoveAccepted, new MoveAcceptedPayload(wire)),
            Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
                WebNetworkId.FromString(PlayerUnitId), wire, yaw, Moving: false)),
        ];
    }

    private static bool HarmsPlayer(SpellDefinition? spell) => ResolvesAsDamage(spell) || LandsOnOpponent(spell);

    private bool WouldHarmCreature(SpellDefinition? spell) => WouldHarmCreature(spell, seenTriggerIds: null);

    private bool WouldHarmCreature(SpellDefinition? spell, HashSet<uint>? seenTriggerIds)
    {
        if (spell is null) return false;
        if (spell.EffectLeapToTarget) return true;
        if (IsStrike(spell)) return true;
        if (LandsOnOpponent(spell)) return true;
        if (spell.AuraPeriodicDamage is not null) return true;
        if (spell.AuraPeriodicTriggerSpellId is not null
            && TriggerWouldHarmCreature(spell.AuraPeriodicTriggerSpellId.Value, seenTriggerIds)) return true;
        if (spell.EffectTriggerSpellId is uint effectTrigger
            && TriggerWouldHarmCreature(effectTrigger, seenTriggerIds)) return true;
        if (spell.AuraTriggerSpellId is uint auraTrigger
            && TriggerWouldHarmCreature(auraTrigger, seenTriggerIds)) return true;
        if (spell.ExtraEffects is not null)
            foreach (var extra in spell.ExtraEffects)
                if (WouldHarmCreature(extra, seenTriggerIds)) return true;
        return false;
    }

    private bool TriggerWouldHarmCreature(uint spellId, HashSet<uint>? seenTriggerIds)
    {
        seenTriggerIds ??= [];
        if (!seenTriggerIds.Add(spellId)) return false;
        return _content.Spells.TryGetValue(spellId, out var triggered) && WouldHarmCreature(triggered, seenTriggerIds);
    }

    private static bool AimsAtOpponent(SpellDefinition? spell) =>
        spell is not null && spell.AuraPeriodicTriggerSpellId is null
        && !LandsOnFriend(spell) && !SpellEffectTargets.IsFriendly(spell.EffectTarget);

    private IReadOnlyList<string> HandOverLastingEffect(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, PlayerBlowKind kind)
    {
        _blows.Send(new PlayerBlow(casterId.Text!, targetId, 0, StrikeResult.Hit, kind, SpellId: spellId,
            ExtraEffect: _resolvingExtra));
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [WebNetworkId.FromString(targetId)], [], spell.Speed, Aimed: false)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> ResolvePrimarySpellEffect(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        if (MountRules.IsMount(spell) && targetId == casterId.Text) return ResolveMount(casterId, spellId, spell, now);
        if (spell.EffectHeal is not null) return ResolveSpellHeal(casterId, targetId, spellId, spell, now);
        if (spell.EffectPowerRestore is not null) return ResolvePowerRestore(casterId, spellId, spell, now);
        if (targetId != casterId.Text && LandsOnFriend(spell) && IsFriendlyPlayer(targetId))
            return HandOverLastingEffect(casterId, targetId, spellId, spell, PlayerBlowKind.Aura);
        if (LandsOnOpponent(spell) && PlayerAlongside(targetId) is not null && IsPlayerOpponent(targetId))
            return HandOverLastingEffect(casterId, targetId, spellId, spell, PlayerBlowKind.Harm);
        if (spell.AuraPeriodicDamage is not null || spell.AuraPeriodicHeal is not null)
            return ResolveAuraApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraPeriodicTriggerSpellId is not null)
            return ResolvePeriodicTriggerApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraStun || spell.AuraSleep)
            return ResolveControlEffect(casterId, targetId, spellId, spell, now);
        if (spell.AuraSilence)
            return ResolveSilenceEffect(casterId, targetId, spellId, spell, now);
        if (spell.AuraSlowPercent is not null && spell.EffectDamage is null)
            return ResolveSlowEffectApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraSpeedupPercent is not null)
            return ResolveSpeedupEffectApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraMeleeAttackSpeedPercent is not null)
            return ResolveMeleeAttackSpeedEffectApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraRangeAttackSpeedPercent is not null)
            return ResolveRangeAttackSpeedEffectApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraChill && !IsStrike(spell))
            return ResolveChillEffectApply(casterId, targetId, spellId, spell, now);
        if (spell.AuraStealth)
            return ResolveStealthEffectApply(casterId, spellId, spell, now);
        if (spell.AuraAbsorbAmount is not null)
            return ResolveAbsorbEffectApply(casterId, spellId, spell, now);
        if (spell.AuraAttributeId is not null)
            return ResolveAttributeModifierApply(casterId, targetId, spellId, spell, now);
        if (spell.EffectRangedWeaponHit)
            return ResolveRangedWeaponDamage(casterId, targetId, spellId, spell, now);
        if (spell.EffectMeleeWeaponHit)
            return ResolveMeleeWeaponDamage(casterId, targetId, spellId, spell, now);
        if (spell.EffectDamage is not null)
            return ResolveSpellDamage(casterId, targetId, spellId, spell, now);
        return ResolveReleaseOnly(casterId, targetId, spellId, spell);
    }

    private IReadOnlyList<string> ResolveReleaseOnly(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private string SpellAbort(JsonElement payload)
    {
        if (_character is null) return Error(WireCodes.ProtocolError.InvalidState, "Character selection is required.");
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        return _spells.CancelPending() | _gathering.Cancel()
            ? Reply(WebMessageTypes.SpellInterrupted, new SpellInterruptedPayload(casterId))
            : Error(WireCodes.ProtocolError.InvalidState, "No cast is in progress.");
    }

    private string ActionBarAssign(JsonElement payload)
    {
        if (!payload.TryGetProperty("slot", out var slotValue) || slotValue.ValueKind != JsonValueKind.Number)
            return Error(WireCodes.ProtocolError.InvalidPayload, "A slot is required.");
        uint id = payload.TryGetProperty("id", out var idValue) && idValue.TryGetUInt32(out var idParsed) ? idParsed : 0;
        int slotType = payload.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.Number
            ? typeValue.GetInt32() : 0;
        int slot = slotValue.GetInt32();
        _actionBar[slot] = (id, slotType);
        return Reply(WebMessageTypes.ActionBarAssigned, ActionSlotPayloadFor(new(slot, (id, slotType))));
    }

    private const int PrimaryBarSlots = 6;

    private static ActionSlotPayload ActionSlotPayloadFor(KeyValuePair<int, (uint Id, int Type)> entry)
    {
        bool shortcut = entry.Key >= PrimaryBarSlots;
        return new ActionSlotPayload(
            shortcut ? "shortcut" : "primary",
            shortcut ? entry.Key - PrimaryBarSlots : entry.Key,
            entry.Value.Id, entry.Value.Type);
    }

}
