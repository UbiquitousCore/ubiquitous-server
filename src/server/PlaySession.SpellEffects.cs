using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private IReadOnlyList<string> ResolveRangedWeaponDamage(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], 0f, Aimed: false)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));

        var distance = DistanceTo(targetId) ?? 0;
        var shot = _combat.LooseSpellShot(
            PlayerUnitId, targetId, distance, now,
            damagePercent: spell.EffectRangedWeaponDamagePercent ?? 100,
            damageBonus: spell.EffectRangedWeaponDamageBonus ?? 0,
            spellId: spellId);
        responses.Add(Reply(WebMessageTypes.AttackShotFired, new AttackShotFiredPayload(
            WebNetworkId.FromString(shot.AttackerId), target, shot.Speed, shot.SpellVisualId)));
        return responses;
    }

    private bool MeetsEquipRequirement(SpellDefinition spell)
    {
        if (spell.WornSlotNeeded < 0) return true;
        if (_character is null || _character.Equipment.Count <= spell.WornSlotNeeded) return false;
        var entry = _character.Equipment[spell.WornSlotNeeded];
        if (entry <= 0) return false;
        if (!_content.Items.TryGetValue((uint)entry, out var item)) return false;
        if (spell.RequiredItemClass != 0 && item.Class != spell.RequiredItemClass) return false;
        if (spell.ItemSubclassesAllowed != 0
            && (spell.ItemSubclassesAllowed & (1u << item.SubClass)) == 0)
            return false;
        return true;
    }

    private IReadOnlyList<string> ResolveSpellDamage(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var target = WebNetworkId.FromString(targetId);
        var outcome = OperatorSpells.All.Contains(spellId)
            ? SpellCastOutcome.Hit
            : _spells.RollDamageOutcome();
        _combat.EnterCombatOnCast(PlayerUnitId, targetId, now);
        // The fight this cast starts is announced (and numbered) before its outcome.
        var entered = new List<string>();
        AppendCombatEntries(entered);
        if (outcome == SpellCastOutcome.Resisted)
        {
            var resistResponses = new List<string>(entered)
            {
                Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [], [target], spell.Speed, Aimed: false)),
            };
            if (spell.CooldownMs > 0)
                resistResponses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
            ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed: false, resistResponses);
            return resistResponses;
        }

        if (outcome == SpellCastOutcome.Miss)
        {
            var missResponses = new List<string>(entered)
            {
                Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
                Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(casterId, target, spellId, WireCodes.Outcome.Miss, 0)),
            };
            if (spell.CooldownMs > 0)
                missResponses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
            ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed: false, missResponses);
            return missResponses;
        }

        var damageAmount = spell.EffectDamageRandom is int damageRandom
            ? _combat.RollAmount(spell.EffectDamage!.Value, spell.EffectDamage.Value + damageRandom)
            : spell.EffectDamage!.Value;
        var spellPowerBonus = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.SpellPower, now);
        damageAmount = CombatService.ApplySpellPowerBonus(damageAmount, spellPowerBonus);
        var spellCritRating = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.SpellCriticalRating, now);
        bool critical = _spells.RollSpellCrit(spellCritRating);
        if (critical)
            damageAmount = (int)Math.Round(damageAmount * SpellService.SpellCriticalDamageMultiplier, MidpointRounding.AwayFromZero);
        var swing = _combat.DamageTarget(PlayerUnitId, targetId, damageAmount, now);
        var responses = new List<string>(entered)
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
        };
        if (swing.OtherPlayer)
        {
            HandOverBlow(swing with { Result = critical ? StrikeResult.Critical : StrikeResult.Hit }, PlayerBlowKind.Spell, spellId);
        }
        else
        {
            MarkUnitValues(target.Text!, new Dictionary<string, int> { [WireCodes.UnitValue.Health] = swing.FinalHealth, [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth }, now);
            responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                casterId, target, spellId, critical ? WireCodes.Outcome.Critical : WireCodes.Outcome.Hit,
                swing.Damage, swing.FinalHealth)));
        }
        if (!swing.Killed) ApplyDamageRiders(casterId, targetId, spellId, spell, now, swing.OtherPlayer, responses);
        if (!swing.Killed && !swing.OtherPlayer)
            ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed: true, responses);
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));

        if (swing.Killed) responses.AddRange(SpellKillSideEffects(casterId, targetId, now));
        return responses;
    }

    private IReadOnlyList<string> SpellKillSideEffects(
        WebNetworkId casterId, string targetId, DateTimeOffset now)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>();
        if (_sight.Entries.TryGetValue(targetId, out var victimEntry))
        {
            var creature = _content.Creatures.TryGetValue(victimEntry, out var found) ? found : null;
            var lootTable = _content.LootTables.TryGetValue(victimEntry, out var table) ? table : null;
            _loot[targetId] = _lootService.Generate(
                checked((int)victimEntry), lootTable, creature?.MoneyDropMin, creature?.MoneyDropMax);
            foreach (var update in _quests.RecordKill(victimEntry, _content.Quests))
                responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));
            responses.AddRange(GrantKillXp(creature, now));
        }

        var carriesLoot = _loot.TryGetValue(targetId, out var spoils) && !LootService.IsEmpty(spoils);
        KillShared(targetId, now);
        HoldCorpse(targetId, now, carriesLoot);
        LeaveDeadTarget(targetId, responses);
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(target)));
        responses.Add(Reply(WebMessageTypes.EntityLootable, new EntityLootablePayload(target, carriesLoot ? WireCodes.Loot.Lootable : WireCodes.Loot.Emptied)));
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(casterId)));
        responses.Add(VictimLeftCombat(target));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(casterId)));
        return responses;
    }

    private string VictimLeftCombat(WebNetworkId victim) =>
        Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(victim));

    private IReadOnlyList<string> ResolveMeleeWeaponDamage(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));

        if (spell.EffectMeleeWeaponSlot == EquipmentSlots.OffHand && !HasWeaponIn(EquipmentSlots.OffHand))
        {
            ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed: false, responses);
            return responses;
        }

        var casterText = casterId.Text!;
        var swing = _combat.StrikeSpellBlow(
            PlayerUnitId, targetId, now,
            damagePercent: spell.EffectMeleeWeaponDamagePercent ?? 100,
            damageBonus: spell.EffectMeleeWeaponDamageBonus ?? 0,
            hitRating: _attributeModifiers.GetFlatAmount(casterText, AttributeIds.HitRating, now),
            critRating: _attributeModifiers.GetFlatAmount(casterText, AttributeIds.CriticalStrike, now),
            strength: _attributeModifiers.GetFlatAmount(casterText, AttributeIds.Strength, now),
            agility: _attributeModifiers.GetFlatAmount(casterText, AttributeIds.Agility, now));

        if (swing.OtherPlayer)
        {
            HandOverBlow(swing, PlayerBlowKind.Spell, spellId);
            return responses;
        }

        responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
            casterId, target, spellId,
            swing.Result == StrikeResult.Miss ? WireCodes.Outcome.Miss
                : swing.Result == StrikeResult.Critical ? WireCodes.Outcome.Critical
                : WireCodes.Outcome.Hit,
            swing.Damage, swing.FinalHealth)));
        MarkUnitValues(target.Text!, new Dictionary<string, int> { [WireCodes.UnitValue.Health] = swing.FinalHealth, [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth }, now);

        if (swing.Killed)
        {
            responses.AddRange(SpellKillSideEffects(casterId, targetId, now));
            return responses;
        }
        bool landed = swing.Result is not (StrikeResult.Miss or StrikeResult.Dodge or StrikeResult.Parry);
        if (landed) ApplyDamageRiders(casterId, targetId, spellId, spell, now, atOtherPlayer: false, responses);
        ResolveTargetExtras(casterId, targetId, spellId, spell, now, landed, responses);
        return responses;
    }

    private bool HasWeaponIn(int slot)
    {
        if (_character is null || _character.Equipment.Count <= slot) return false;
        var entry = _character.Equipment[slot];
        if (entry <= 0) return false;
        return _content.Items.TryGetValue((uint)entry, out var item) && item.Class == ItemClasses.Weapons;
    }

    private IReadOnlyList<string> ResolvePowerRestore(
        WebNetworkId casterId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [casterId], [], spell.Speed, Aimed: false)),
        };
        var amount = spell.EffectPowerRestoreRandom is int spread
            ? _combat.RollAmount(spell.EffectPowerRestore!.Value, spell.EffectPowerRestore.Value + spread)
            : spell.EffectPowerRestore!.Value;
        var wisdomBonus = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.Wisdom, now);
        if (_spells.RestorePower(spell.EffectPowerKind, amount, wisdomBonus) is not null)
            MarkUnitValues(casterId.Text!, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = _spells.Power }, now);
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private IReadOnlyList<string> ResolveSpellHeal(
        WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        bool atFriend = targetId != casterId.Text && IsFriendlyPlayer(targetId);
        var target = atFriend ? WebNetworkId.FromString(targetId) : casterId;
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
        };

        var healAmount = spell.EffectHealRandom is int healRandom
            ? _combat.RollAmount(spell.EffectHeal!.Value, spell.EffectHeal.Value + healRandom)
            : spell.EffectHeal!.Value;
        var healingPowerBonus = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.HealingPower, now);
        healAmount = CombatService.ApplyHealingPowerBonus(healAmount, healingPowerBonus);
        if (atFriend)
        {
            _blows.Send(new PlayerBlow(casterId.Text!, targetId, healAmount, StrikeResult.Hit, PlayerBlowKind.Heal, SpellId: spellId));
            return responses;
        }

        var staminaBonus = _attributeModifiers.GetFlatAmount(casterId.Text!, AttributeIds.Stamina, now);
        var healed = _combat.HealPlayer(healAmount, staminaBonus);
        if (healed is null) return responses;

        var healthValues = new Dictionary<string, int> { [WireCodes.UnitValue.Health] = healed.FinalHealth, [WireCodes.UnitValue.MaxHealth] = healed.MaxHealth };
        MarkUnitValues(casterId.Text!, healthValues, now);
        responses.AddRange(
        [
            Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
                casterId, casterId, spellId, WireCodes.Outcome.Heal, healed.Applied, healed.FinalHealth)),
        ]);
        return responses;
    }
}
