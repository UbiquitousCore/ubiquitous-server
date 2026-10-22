using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private sealed record PendingCreatureCast(uint SpellId, SpellDefinition Spell, string TargetId, DateTimeOffset LandsAt);

    // Own choice: the spell rows name who an area spell hits but not how far it reaches.
    private const double CreatureAreaAroundCaster = 15.0;
    private const double CreatureAreaAroundTarget = 6.0;

    private readonly Dictionary<string, PendingCreatureCast> _creatureCasts = new(StringComparer.Ordinal);
    private readonly PeriodicTriggerService _creaturePulses = new();
    private readonly Dictionary<string, DateTimeOffset> _summonCrumblesAt = new(StringComparer.Ordinal);
    private readonly List<(uint Entry, DomainPosition At)> _deathSplits = [];

    private void TickCreatureCasts(string playerId, DateTimeOffset now, List<string> responses)
    {
        foreach (var (casterId, cast) in _creatureCasts.ToArray())
        {
            if (BodyGone(casterId))
            {
                _creatureCasts.Remove(casterId);
                responses.Add(Reply(WebMessageTypes.SpellInterrupted,
                    new SpellInterruptedPayload(WebNetworkId.FromString(casterId))));
                continue;
            }

            if (now < cast.LandsAt) continue;
            _creatureCasts.Remove(casterId);
            LandCreatureSpell(casterId, cast.TargetId, cast.SpellId, cast.Spell, now, responses);
        }
    }

    private void LandCreatureSpell(
        string casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now, List<string> responses)
    {
        responses.Add(Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(
            spellId, WebNetworkId.FromString(casterId), [WebNetworkId.FromString(targetId)], [], spell.Speed,
            Aimed: false)));

        LandCreatureEffect(casterId, targetId, spellId, spell, PlayerBlow.MainEffect, now, responses);
        var extras = spell.ExtraEffects ?? [];
        for (var index = 0; index < extras.Count; index++)
            LandCreatureEffect(casterId, targetId, spellId, extras[index], index, now, responses);
    }

    private void LandCreatureEffect(
        string casterId, string targetId, uint spellId, SpellDefinition effect, int effectIndex,
        DateTimeOffset now, List<string> responses)
    {
        if (AreaOf(casterId, targetId, effect.EffectTarget) is var (centre, reach))
        {
            foreach (var playerId in EnemiesOf(casterId))
            {
                if (PositionOfPlayer(playerId) is { } standing && Apart(centre, standing) <= reach)
                    HitPlayerWithCreatureSpell(casterId, playerId, spellId, effect, effectIndex, now, responses);
            }

            return;
        }

        if (targetId.StartsWith("player:", StringComparison.Ordinal))
            HitPlayerWithCreatureSpell(casterId, targetId, spellId, effect, effectIndex, now, responses);
        else
            LandOnCreature(casterId, targetId, spellId, effect, now, responses);
    }

    private (DomainPosition Centre, double Reach)? AreaOf(string casterId, string targetId, int effectTarget)
    {
        if (effectTarget == SpellEffectTargets.AreaEnemy)
            return CreaturePosition(casterId) is { } at ? (at, CreatureAreaAroundCaster) : null;
        if (effectTarget is SpellEffectTargets.AreaEnemyOfTarget or SpellEffectTargets.PositionAreaEnemy)
        {
            var at = targetId.StartsWith("player:", StringComparison.Ordinal)
                ? PositionOfPlayer(targetId)
                : CreaturePosition(targetId);
            return at is { } centre ? (centre, CreatureAreaAroundTarget) : null;
        }

        return null;
    }

    private DomainPosition? CreaturePosition(string wireId) =>
        _shared is not null && _shared.TryGetPosition(wireId, out var at) ? at : null;

    private DomainPosition? PositionOfPlayer(string playerId)
    {
        if (_character is not null && string.Equals(playerId, PlayerUnitId, StringComparison.Ordinal))
            return _world?.PlayerPosition;
        return PlayerAlongside(playerId)?.PlayerPosition;
    }

    private IReadOnlyList<string> EnemiesOf(string creatureId)
    {
        var enemies = new List<string>();
        if (_character is not null && !_combat.IsPlayerDead) enemies.Add(PlayerUnitId);
        if (_shared is null) return enemies;
        foreach (var fighter in _shared.Fighters(creatureId))
        {
            if (enemies.Contains(fighter, StringComparer.Ordinal)) continue;
            if (PlayerAlongside(fighter) is { IsAlive: true }) enemies.Add(fighter);
        }

        return enemies;
    }

    private string AnyEnemyOf(string creatureId, string fallback)
    {
        var enemies = EnemiesOf(creatureId);
        return enemies.Count == 0 ? fallback : enemies[_combat.PickOne(enemies.Count)];
    }

    private void HitPlayerWithCreatureSpell(
        string casterId, string playerId, uint spellId, SpellDefinition effect, int effectIndex,
        DateTimeOffset now, List<string> responses)
    {
        if (_character is not null && string.Equals(playerId, PlayerUnitId, StringComparison.Ordinal))
        {
            TakeCreatureSpell(casterId, spellId, effect, effectIndex, now, responses);
            return;
        }

        _blows.Send(new PlayerBlow(casterId, playerId, 0, StrikeResult.Hit, PlayerBlowKind.CreatureSpell,
            SpellId: spellId, ExtraEffect: effectIndex));
    }

    private void TakeRelayedCreatureSpell(PlayerBlow blow, DateTimeOffset now, List<string> responses)
    {
        if (!_content.Spells.TryGetValue(blow.SpellId, out var cast)) return;
        var effect = blow.ExtraEffect == PlayerBlow.MainEffect ? cast : cast.ExtraEffects?.ElementAtOrDefault(blow.ExtraEffect);
        if (effect is not null) TakeCreatureSpell(blow.AttackerId, blow.SpellId, effect, blow.ExtraEffect, now, responses);
    }

    private SpellStacks? _stacksOnMe;

    private void TakeCreatureSpell(
        string casterId, uint spellId, SpellDefinition effect, int effectIndex, DateTimeOffset now,
        List<string> responses)
    {
        if (_character is null || _combat.IsPlayerDead) return;
        _stacksOnMe ??= new SpellStacks(_content.SpellStackLimits);
        effect = SpellStacks.Scaled(effect, _stacksOnMe.Land(spellId, effect, adds: effectIndex == PlayerBlow.MainEffect, now));
        var playerId = PlayerUnitId;
        var caster = WebNetworkId.FromString(casterId);

        if (CreatureSpellDamage(casterId, effect) is int damage and > 0)
        {
            damage = CombatService.ApplyArmorMitigation(
                damage, _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Armor, now));
            var swing = _combat.DamageTarget(
                casterId, playerId, damage, now, _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now));
            MarkUnitValues(playerId, new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Health] = swing.FinalHealth,
                [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth,
            }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                caster, WebNetworkId.FromString(playerId), spellId, WireCodes.Outcome.Hit, swing.Damage,
                swing.FinalHealth, WireAbsorbed(swing))));
            if (swing.Killed)
            {
                AnnounceScriptedKill(casterId, playerId, playerId, responses);
                return;
            }
        }

        var lasting = effect with
        {
            EffectDamage = null, EffectDamageRandom = null, EffectHeal = null, EffectMeleeWeaponHit = false,
            ExtraEffects = null,
        };
        if (!LeavesSomethingBehind(lasting)) return;
        responses.AddRange(ResolvePrimarySpellEffect(caster, playerId, spellId, lasting, now).Where(message =>
        {
            var type = WebMessageJson.Deserialize(message).Type;
            return type != WebMessageTypes.SpellReleased && type != WebMessageTypes.SpellCooldownStarted;
        }));
    }

    private static bool LeavesSomethingBehind(SpellDefinition effect) =>
        effect.AuraPeriodicDamage is not null || effect.AuraStun || effect.AuraSleep || effect.AuraSilence
        || effect.AuraSlowPercent is not null || effect.AuraSpeedupPercent is not null
        || effect.AuraMeleeAttackSpeedPercent is not null || effect.AuraRangeAttackSpeedPercent is not null
        || effect.AuraAttributeId is not null || effect.AuraChill;

    private int? CreatureSpellDamage(string casterId, SpellDefinition effect)
    {
        if (effect.EffectDamage is int baseDamage)
        {
            return effect.EffectDamageRandom is int spread
                ? _combat.RollAmount(baseDamage, baseDamage + spread)
                : baseDamage;
        }

        if (!effect.EffectMeleeWeaponHit || CreatureFor(casterId) is not { MinDamage: int min, MaxDamage: int max })
            return null;
        var swing = _combat.RollAmount(min, Math.Max(min, max));
        return (swing * (effect.EffectMeleeWeaponDamagePercent ?? 100) / 100) + (effect.EffectMeleeWeaponDamageBonus ?? 0);
    }

    private void LandOnCreature(
        string casterId, string targetId, uint spellId, SpellDefinition effect, DateTimeOffset now, List<string> responses)
    {
        if (BodyGone(targetId)) return;
        var caster = WebNetworkId.FromString(casterId);
        var target = WebNetworkId.FromString(targetId);

        if (effect.EffectHeal is int heal)
        {
            var restored = _combat.RestoreHealth(targetId, heal);
            if (restored.Applied > 0)
                MarkUnitValues(targetId, new Dictionary<string, int>
                {
                    [WireCodes.UnitValue.Health] = restored.FinalHealth,
                    [WireCodes.UnitValue.MaxHealth] = restored.MaxHealth,
                }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                caster, target, spellId, WireCodes.Outcome.Heal, restored.Applied, restored.FinalHealth)));
            return;
        }

        if (effect.EffectDamage is int damage)
        {
            var swing = _combat.DamageTarget(casterId, targetId, damage, now);
            MarkUnitValues(targetId, new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Health] = swing.FinalHealth,
                [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth,
            }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                caster, target, spellId, WireCodes.Outcome.Hit, swing.Damage, swing.FinalHealth, WireAbsorbed(swing))));
            if (swing.Killed) CreatureFalls(targetId, casterId, now, responses);
            return;
        }

        if (effect.AuraPeriodicTriggerSpellId is uint pulse)
            _creaturePulses.Apply(targetId, spellId, pulse, effect.AuraPeriodicTriggerChancePercent ?? 100, now,
                PeriodicTriggerService.TicksFor(effect.DurationMs));
        if (effect.AuraAbsorbAmount is int shield)
            _combat.ShieldUnit(targetId, effect.AuraAbsorbPercent ?? 100, shield);

        var lasts = effect.DurationMs > 0 ? effect.DurationMs : 0;
        responses.Add(Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(
            spellId, target, caster, Harmful: 0, MaxDuration: lasts, Remaining: lasts)));
    }

    private void EndCreatureAura(string wireId, uint spellId, List<string> responses)
    {
        _creaturePulses.End(wireId, spellId);
        if (_content.Spells.TryGetValue(spellId, out var spell) && spell.AuraAbsorbAmount is not null)
            _combat.UnshieldUnit(wireId);
        responses.Add(AuraEnded(wireId, wireId, spellId));
    }

    private void TickCreaturePulses(string playerId, DateTimeOffset now, List<string> responses)
    {
        foreach (var tick in _creaturePulses.Tick(now))
        {
            var wearer = tick.TargetId;
            if (BodyGone(wearer))
            {
                _creaturePulses.Clear(wearer);
                continue;
            }

            if (_spells.RollTriggerChance(tick.ChancePercent)
                && _content.Spells.TryGetValue(tick.TriggerSpellId, out var triggered))
            {
                var victim = _combat.RetaliationTarget(wearer) ?? playerId;
                LandCreatureSpell(wearer, victim, tick.TriggerSpellId, triggered, now, responses);
            }

            if (tick.Expired) responses.Add(AuraEnded(wearer, wearer, tick.SpellId));
        }
    }

    private void CreatureFalls(string fallenId, string killerId, DateTimeOffset now, List<string> responses)
    {
        _creaturePulses.Clear(fallenId);
        _creatureCasts.Remove(fallenId);
        _summonCrumblesAt.Remove(fallenId);
        if (string.Equals(fallenId, killerId, StringComparison.Ordinal))
        {
            responses.AddRange(SpellKillSideEffects(WebNetworkId.FromString(killerId), fallenId, now));
            return;
        }

        KillShared(fallenId, now);
        HoldCorpse(fallenId, now, carriesLoot: false);
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(WebNetworkId.FromString(fallenId))));
    }

    private void TickCrumblingSummons(DateTimeOffset now, List<string> responses)
    {
        foreach (var (summonId, at) in _summonCrumblesAt.ToArray())
        {
            if (now < at) continue;
            _summonCrumblesAt.Remove(summonId);
            if (BodyGone(summonId)) continue;
            _combat.MarkDead(summonId);
            _creaturePulses.Clear(summonId);
            KillShared(summonId, now);
            Forget(summonId);
            responses.Add(Reply(WebMessageTypes.EntityRemoved, new EntityRemovedPayload(WebNetworkId.FromString(summonId))));
        }
    }

    private void NoteDeathSplits(string wireId)
    {
        if (!_sight.Contains(wireId) || CreaturePosition(wireId) is not { } at) return;
        foreach (var split in ScriptFor(wireId).DiesInto) _deathSplits.Add((split, at));
    }

    private void TickDeathSplits(List<string> responses)
    {
        if (_deathSplits.Count == 0) return;
        var due = _deathSplits.ToArray();
        _deathSplits.Clear();
        for (var i = 0; i < due.Length; i++)
        {
            var (entry, at) = due[i];
            var angle = i * Math.PI;
            SummonAt(entry, new DomainPosition((float)(at.X + Math.Cos(angle) * 2), at.Y, (float)(at.Z + Math.Sin(angle) * 2)), responses);
        }
    }
}
