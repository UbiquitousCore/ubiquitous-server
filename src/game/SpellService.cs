using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum SpellRefusalReason
{
    UnknownSpell,
    NoEffectData,
    OnCooldown,
    InsufficientPower,
    AlreadyCasting,
}

public sealed record SpellCastRefusal(SpellRefusalReason Error);

public enum SpellCastOutcome
{
    Hit,
    Miss,
    Resisted,
}

public sealed record SpellCastStarted(SpellDefinition Spell, string? TargetId, bool IsInstant);

public sealed record SpellCastReady(uint SpellId, SpellDefinition Spell, string? TargetId);

public sealed class SpellService
{
    public const int PlayerMaxPower = 100;
    public const int PowerRegenPerInterval = 10;
    public static readonly TimeSpan PowerRegenInterval = TimeSpan.FromSeconds(8);
    public const int PowerRegenPerRating = 1;
    public const double MissChance = 0.05;
    public const double ResistChance = 0.08;
    public const double SpellCritChancePerRating = 0.001;
    public const double MaxSpellCritChance = 0.3;
    public const double SpellCriticalDamageMultiplier = 2.0;

    private readonly HashSet<uint> _known;
    private readonly Dictionary<uint, DateTimeOffset> _cooldownUntil = new();
    private readonly Func<double> _rollUnit;
    private int _power = PlayerMaxPower;
    private DateTimeOffset _nextPowerRegen;
    private (uint SpellId, SpellDefinition Spell, string? TargetId, DateTimeOffset ReadyAt)? _pending;
    private int _classId;
    private int _level = 1;
    private int _baseIntellect;

    public const int ManaPerWisdom = 15;

    public SpellService(IEnumerable<uint> knownSpells, Func<double>? rollUnit = null)
    {
        _known = [.. knownSpells];
        _rollUnit = rollUnit ?? (() => Random.Shared.NextDouble());
    }

    public int Power => _power;

    public bool IsCasting => _pending is not null;

    public void EnterAsCharacter(int classId, int level, IEnumerable<uint>? knownSpells = null, int? baseIntellect = null)
    {
        _classId = classId;
        _level = level;
        if (baseIntellect is int intellect) _baseIntellect = Math.Max(0, intellect);
        _power = EffectiveMaxPower(0);
        if (knownSpells is null) return;
        _known.Clear();
        foreach (var spellId in knownSpells) _known.Add(spellId);
    }

    public IReadOnlySet<uint> Known => _known;

    public void ReplaceKnown(IEnumerable<uint> knownSpells)
    {
        _known.Clear();
        foreach (var spellId in knownSpells) _known.Add(spellId);
    }

    public static bool HasResolvableEffect(SpellDefinition spell) =>
        spell.EffectDamage is not null || spell.EffectHeal is not null
        || spell.AuraPeriodicDamage is not null || spell.AuraPeriodicHeal is not null
        || spell.AuraStun || spell.AuraSleep || spell.AuraSilence || spell.AuraAttributeId is not null
        || spell.AuraSlowPercent is not null || spell.AuraSpeedupPercent is not null || spell.AuraChill
        || spell.AuraMeleeAttackSpeedPercent is not null || spell.AuraRangeAttackSpeedPercent is not null
        || spell.EffectRangedWeaponHit || spell.EffectMeleeWeaponHit
        || spell.AuraStealth || spell.AuraAbsorbAmount is not null
        || spell.EffectPowerRestore is not null
        || spell.AuraTriggerSpellId is not null || spell.AuraPeriodicTriggerSpellId is not null
        || spell.EffectTriggerSpellId is not null || spell.EffectLeapToTarget
        || spell.AuraMountId is not null
        || (spell.ExtraEffects?.Any(HasResolvableEffect) ?? false);

    public int? RestorePower(int powerKind, int amount, int wisdomBonus = 0)
    {
        if (powerKind != PowerKinds.ForClass(_classId) || amount <= 0) return null;
        var before = _power;
        _power = Math.Min(EffectiveMaxPower(wisdomBonus), _power + amount);
        return _power - before;
    }

    public SpellCastStarted? TryBeginCast(
        uint spellId, IReadOnlyDictionary<uint, SpellDefinition> spells, string? targetId,
        DateTimeOffset now, out SpellCastRefusal? refusal, int wisdomBonus = 0)
    {
        refusal = null;
        if (!_known.Contains(spellId) || !spells.TryGetValue(spellId, out var spell))
        {
            refusal = new SpellCastRefusal(SpellRefusalReason.UnknownSpell);
            return null;
        }

        if (_pending is not null)
        {
            refusal = new SpellCastRefusal(SpellRefusalReason.AlreadyCasting);
            return null;
        }

        if (!HasResolvableEffect(spell)
            || (spell.PowerCost is null && spell.CooldownMs <= 0 && spell.CastTimeMs <= 0))
        {
            refusal = new SpellCastRefusal(SpellRefusalReason.NoEffectData);
            return null;
        }

        if (_cooldownUntil.TryGetValue(spellId, out var readyAt) && now < readyAt)
        {
            refusal = new SpellCastRefusal(SpellRefusalReason.OnCooldown);
            return null;
        }

        if (_power < (spell.PowerCost ?? 0))
        {
            refusal = new SpellCastRefusal(SpellRefusalReason.InsufficientPower);
            return null;
        }

        bool instant = spell.CastTimeMs <= 0;
        if (instant)
        {
            Resolve(spellId, spell, now, wisdomBonus);
        }
        else
        {
            _pending = (spellId, spell, targetId, now + TimeSpan.FromMilliseconds(spell.CastTimeMs));
        }

        return new SpellCastStarted(spell, targetId, instant);
    }

    public SpellCastReady? TakeFinished(DateTimeOffset now)
    {
        if (_pending is not { } pending || now < pending.ReadyAt) return null;
        _pending = null;
        return new SpellCastReady(pending.SpellId, pending.Spell, pending.TargetId);
    }

    // A finished cast costs nothing until the caller knows it lands; a cast that fails there is free.
    public void Spend(SpellCastReady finished, DateTimeOffset now, int wisdomBonus = 0) =>
        Resolve(finished.SpellId, finished.Spell, now, wisdomBonus);

    public bool CancelPending()
    {
        if (_pending is null) return false;
        _pending = null;
        return true;
    }

    public SpellCastOutcome RollDamageOutcome()
    {
        var roll = _rollUnit();
        if (roll < ResistChance) return SpellCastOutcome.Resisted;
        return roll < ResistChance + MissChance ? SpellCastOutcome.Miss : SpellCastOutcome.Hit;
    }

    public bool RollTriggerChance(int percent) => _rollUnit() < percent / 100.0;

    public bool RollSpellCrit(int spellCritRating) => _rollUnit() < SpellCritChance(spellCritRating);

    public static double SpellCritChance(int spellCritRating) =>
        Math.Clamp(Math.Max(0, spellCritRating) * SpellCritChancePerRating, 0, MaxSpellCritChance);

    public const double SpellHitChance = 1.0 - ResistChance - MissChance;

    public int? PowerRegen(DateTimeOffset now, int wisdomBonus = 0, int manaRegenRating = 0)
    {
        var max = EffectiveMaxPower(wisdomBonus);
        if (_power >= max) return null;
        if (now < _nextPowerRegen) return null;
        _nextPowerRegen = now + PowerRegenInterval;
        var amount = PowerRegenPerInterval + Math.Max(0, manaRegenRating) * PowerRegenPerRating;
        _power = Math.Min(max, _power + amount);
        return _power;
    }

    public int EffectiveMaxPower(int wisdomBonus)
    {
        var manaBase = PlayerStatCurves.BaseMaxPower(_classId, _level, fallback: 0);
        if (manaBase <= 0) return PlayerMaxPower;
        var wisdom = PlayerStatCurves.AttributesAt(_classId, _level, new StartingAttributes(0, 0, 0, _baseIntellect)).Intellect;
        return Math.Max(1, manaBase + (wisdom + wisdomBonus) * ManaPerWisdom);
    }

    public void RestoreFullPower(int wisdomBonus = 0) => _power = EffectiveMaxPower(wisdomBonus);

    private void Resolve(uint spellId, SpellDefinition spell, DateTimeOffset now, int wisdomBonus = 0)
    {
        _power -= spell.PowerCost ?? 0;
        if (spell.CooldownMs > 0) _cooldownUntil[spellId] = now + TimeSpan.FromMilliseconds(spell.CooldownMs);
        if (_power < EffectiveMaxPower(wisdomBonus)) _nextPowerRegen = now + PowerRegenInterval;
    }
}
