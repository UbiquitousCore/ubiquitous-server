using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private HashSet<uint> ClearEffectsOn(string unitId) =>
    [
        .. _auras.Clear(unitId),
        .. _periodicTrigger.Clear(unitId),
        .. _control.Clear(unitId),
        .. _silence.Clear(unitId),
        .. _attributeModifiers.Clear(unitId),
        .. _slow.Clear(unitId),
        .. _speedup.Clear(unitId),
        .. _meleeAttackSpeed.Clear(unitId),
        .. _rangeAttackSpeed.Clear(unitId),
        .. _chill.Clear(unitId),
        .. _stealth.Clear(unitId),
        .. _absorb.Clear(unitId),
    ];

    private IReadOnlyList<string> ResolveAuraApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        bool isHeal = spell.AuraPeriodicHeal is not null;
        int perTick = isHeal ? spell.AuraPeriodicHeal!.Value : spell.AuraPeriodicDamage!.Value;
        _auras.Apply(targetId, spellId, perTick, isHeal, now, casterId.Text);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = checked((int)AuraService.Duration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: isHeal ? 0 : 1, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> ResolveControlEffect(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _control.Apply(targetId, spellId, spell.DurationMs, now, casterId.Text);
        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)ControlEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickControlEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _control.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveSilenceEffect(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _silence.Apply(targetId, spellId, spell.DurationMs, now, casterId.Text);
        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)ControlEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickSilenceEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _silence.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveSlowEffectApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        int percent = spell.AuraSlowPercent!.Value;
        _slow.Apply(targetId, spellId, percent, spell.DurationMs, now, casterId.Text);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)SlowEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickSlowEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _slow.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveSpeedupEffectApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        int percent = spell.AuraSpeedupPercent!.Value;
        _speedup.Apply(targetId, spellId, percent, spell.DurationMs, now, casterId.Text);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)SpeedupEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: percent < 0 ? 1 : 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickSpeedupEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _speedup.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveMeleeAttackSpeedEffectApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        int percent = spell.AuraMeleeAttackSpeedPercent!.Value;
        _meleeAttackSpeed.Apply(targetId, spellId, percent, spell.DurationMs, now, casterId.Text);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)SpeedupEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: percent < 0 ? 1 : 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickMeleeAttackSpeedEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _meleeAttackSpeed.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveRangeAttackSpeedEffectApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        int percent = spell.AuraRangeAttackSpeedPercent!.Value;
        _rangeAttackSpeed.Apply(targetId, spellId, percent, spell.DurationMs, now);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)SpeedupEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: percent < 0 ? 1 : 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickRangeAttackSpeedEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        var responses = new List<string>();
        foreach (var expired in _rangeAttackSpeed.TickExpired(now))
        {
            responses.Add(Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(expired.SpellId, WebNetworkId.FromString(expired.TargetId),
                casterId)));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveChillEffectApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            ApplyChill(casterId, targetId, spellId, spell, now),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private string ApplyChill(WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _chill.Apply(targetId, spellId, spell.DurationMs, now, casterId.Text);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)ChillEffectService.DefaultDuration.TotalMilliseconds);
        return Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, WebNetworkId.FromString(targetId), casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs));
    }

    private void ApplyDamageRiders(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now, bool atOtherPlayer,
        List<string> responses)
    {
        if (!spell.AuraChill && spell.AuraSlowPercent is null) return;
        if (atOtherPlayer)
        {
            _blows.Send(new PlayerBlow(casterId.Text!, targetId, 0, StrikeResult.Hit, PlayerBlowKind.Harm, SpellId: spellId));
            return;
        }
        if (spell.AuraSlowPercent is int percent)
            _slow.Apply(targetId, spellId, percent, spell.DurationMs, now, casterId.Text);
        if (spell.AuraChill)
        {
            responses.Add(ApplyChill(casterId, targetId, spellId, spell, now));
            return;
        }
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)SlowEffectService.DefaultDuration.TotalMilliseconds);
        responses.Add(Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, WebNetworkId.FromString(targetId), casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs)));
    }

    private void ReportToAuraBoard(object payload)
    {
        if (_shared is null) return;
        switch (payload)
        {
            case AuraAppliedPayload applied:
                _shared.Auras.Applied(this, new HeldAura(IdText(applied.UnitId), IdText(applied.CasterId), applied.SpellId,
                    applied.Remaining, applied.MaxDuration, applied.Harmful != 0),
                    Now);
                break;
            case AuraEndedPayload ended:
                _shared.Auras.Ended(this, IdText(ended.UnitId), ended.SpellId);
                break;
            case UnitDiedPayload died:
                _shared.Auras.Died(IdText(died.NetworkId));
                break;
        }
    }

    private static string IdText(WebNetworkId id) => id.Text ?? id.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private IReadOnlyList<HeldAuraPayload>? AurasWornBy(string unitId)
    {
        var worn = _shared?.Auras.On(unitId, Now);
        if (worn is not { Count: > 0 }) return null;
        return [.. worn.Select(aura => new HeldAuraPayload(
            aura.SpellId, WebNetworkId.FromString(aura.CasterId), aura.Harmful ? 1 : 0,
            aura.MaxDurationMs, aura.RemainingMs))];
    }

    public void ForgetReportedAuras() => _shared?.Auras.Forget(this);

    private static IEnumerable<string> OnceEach(IEnumerable<string> messages)
    {
        var seen = new HashSet<(string?, string?, uint)>();
        foreach (var message in messages)
        {
            var root = JsonDocument.Parse(message).RootElement;
            if (root.GetProperty("type").GetString() == WebMessageTypes.AuraEnded)
            {
                var payload = root.GetProperty("payload");
                var key = (payload.GetProperty("casterId").GetString(), payload.GetProperty("unitId").GetString(),
                    payload.GetProperty("spellId").GetUInt32());
                if (!seen.Add(key)) continue;
            }
            yield return message;
        }
    }

    private string AuraEnded(string? casterId, string unitId, uint spellId) =>
        Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(
            spellId, WebNetworkId.FromString(unitId), WebNetworkId.FromString(casterId ?? PlayerUnitId)));

    private IReadOnlyList<string> ResolveAbsorbEffectApply(
        WebNetworkId casterId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _absorb.Apply(casterId.Text!, spellId, spell.AuraAbsorbAmount!.Value, spell.AuraAbsorbPercent ?? 100,
            spell.DurationMs, now);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)AbsorbEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [casterId], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, casterId, casterId, Harmful: 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickAbsorbEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        return _absorb.TickExpired(now)
            .Select(ended => Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(ended.SpellId, WebNetworkId.FromString(ended.TargetId),
                casterId)))
            .ToList();
    }

    private IReadOnlyList<string> ResolveStealthEffectApply(
        WebNetworkId casterId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var casterText = casterId.Text!;
        _stealth.Apply(casterText, spellId, spell.DurationMs, now);

        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)StealthEffectService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [casterId], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, casterId, casterId, Harmful: 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickStealthEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        var responses = new List<string>();
        foreach (var expired in _stealth.TickExpired(now))
        {
            responses.Add(Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(expired.SpellId, WebNetworkId.FromString(expired.TargetId),
                casterId)));
        }
        return responses;
    }

    private IReadOnlyList<string> TickChillEffects(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _chill.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> ResolveAttributeModifierApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        int attributeId = spell.AuraAttributeId!.Value;
        int amount = spell.AuraAttributeAmount ?? 0;
        _attributeModifiers.Apply(targetId, spellId, attributeId, amount, spell.AuraAttributeIsPercent, spell.DurationMs, now, casterId.Text);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = spell.DurationMs > 0
            ? spell.DurationMs
            : checked((int)AttributeModifierService.DefaultDuration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: amount < 0 ? 1 : 0, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickAttributeModifiers(DateTimeOffset now)
    {
        if (_character is null) return [];
        var responses = new List<string>();
        foreach (var expired in _attributeModifiers.TickExpired(now))
        {
            responses.Add(AuraEnded(expired.CasterId, expired.TargetId, expired.SpellId));
        }
        return responses;
    }

    private IReadOnlyList<string> TickAuras(DateTimeOffset now)
    {
        if (_character is null) return [];
        var playerId = PlayerUnitId;
        var casterId = WebNetworkId.FromString(playerId);
        var responses = new List<string>();
        foreach (var tick in _auras.Tick(now))
        {
            if (tick.IsHeal)
            {
                var healingPowerBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.HealingPower, now);
                var healed = _combat.HealPlayer(
                    CombatService.ApplyHealingPowerBonus(tick.PerTick, healingPowerBonus),
                    _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now));
                if (healed is not null)
                {
                    var target = WebNetworkId.FromString(tick.TargetId);
                    var healthValues = new Dictionary<string, int> { [WireCodes.UnitValue.Health] = healed.FinalHealth, [WireCodes.UnitValue.MaxHealth] = healed.MaxHealth };
                    responses.Add(Reply(WebMessageTypes.SpellOutcome,
                        new SpellOutcomePayload(
                            WebNetworkId.FromString(tick.CasterId ?? playerId), target, tick.SpellId,
                            WireCodes.Outcome.PeriodicHeal, healed.Applied, healed.FinalHealth)));
                    MarkUnitValues(target.Text!, healthValues, now);
                }
            }
            else if (string.Equals(tick.TargetId, playerId, StringComparison.Ordinal))
            {
                TakeDamageOverTimeTick(tick, now, responses);
            }
            else if (!_combat.IsDead(tick.TargetId) && _sight.Contains(tick.TargetId))
            {
                var target = WebNetworkId.FromString(tick.TargetId);
                var tickSpellPowerBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.SpellPower, now);
                var tickDamage = CombatService.ApplySpellPowerBonus(tick.PerTick, tickSpellPowerBonus);
                var swing = _combat.DamageTarget(PlayerUnitId, tick.TargetId, tickDamage, now);
                AppendCombatEntries(responses);
                responses.Add(Reply(WebMessageTypes.SpellOutcome,
                    new SpellOutcomePayload(
                        casterId, target, tick.SpellId, WireCodes.Outcome.Hit, swing.Damage, swing.FinalHealth)));
                MarkUnitValues(target.Text!, new Dictionary<string, int> { [WireCodes.UnitValue.Health] = swing.FinalHealth, [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth }, now);

                if (swing.Killed)
                {
                    if (_sight.Entries.TryGetValue(tick.TargetId, out var victimEntry))
                    {
                        var creature = _content.Creatures.TryGetValue(victimEntry, out var found) ? found : null;
                        var lootTable = _content.LootTables.TryGetValue(victimEntry, out var table) ? table : null;
                        _loot[tick.TargetId] = _lootService.Generate(
                            checked((int)victimEntry), lootTable, creature?.MoneyDropMin, creature?.MoneyDropMax);
                        foreach (var update in _quests.RecordKill(victimEntry, _content.Quests))
                            responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));
                        responses.AddRange(GrantKillXp(creature, now));
                    }

                    var carriesLoot = _loot.TryGetValue(tick.TargetId, out var spoils) && !LootService.IsEmpty(spoils);
                    KillShared(tick.TargetId, now);
                    HoldCorpse(tick.TargetId, now, carriesLoot);
                    LeaveDeadTarget(tick.TargetId, responses);
                    responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(target)));
                    responses.Add(Reply(WebMessageTypes.EntityLootable, new EntityLootablePayload(target, carriesLoot ? WireCodes.Loot.Lootable : WireCodes.Loot.Emptied)));
                    responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(casterId)));
                    responses.Add(VictimLeftCombat(target));
                    responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(casterId)));
                    _auras.Clear(tick.TargetId);
                    _periodicTrigger.Clear(tick.TargetId);
                }
            }

            if (tick.Expired) responses.Add(AuraEnded(tick.CasterId, tick.TargetId, tick.SpellId));
        }

        return responses;
    }

    private IReadOnlyList<string> ResolvePeriodicTriggerApply(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _periodicTrigger.Apply(
            targetId, spellId, spell.AuraPeriodicTriggerSpellId!.Value, spell.AuraPeriodicTriggerChancePercent ?? 0, now);

        var target = WebNetworkId.FromString(targetId);
        int durationMs = checked((int)AuraService.Duration.TotalMilliseconds);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(spellId, target, casterId, Harmful: 1, MaxDuration: durationMs, Remaining: durationMs)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> TickPeriodicTriggers(DateTimeOffset now)
    {
        if (_character is null) return [];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        var responses = new List<string>();
        foreach (var tick in _periodicTrigger.Tick(now))
        {
            if (tick.TriggerSpellId != 0
                && _spells.RollTriggerChance(tick.ChancePercent)
                && _content.Spells.TryGetValue(tick.TriggerSpellId, out var triggeredSpell))
            {
                responses.AddRange(ResolveSpellEffect(casterId, tick.TargetId, tick.TriggerSpellId, triggeredSpell, now));
            }

            if (tick.Expired)
            {
                responses.Add(Reply(WebMessageTypes.AuraEnded,
                    new AuraEndedPayload(tick.SpellId, WebNetworkId.FromString(tick.TargetId), casterId)));
            }
        }

        return responses;
    }
}
