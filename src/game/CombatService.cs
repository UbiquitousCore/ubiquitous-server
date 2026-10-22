using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record AttackStartResult(string AttackerId, string TargetId, string? StoppedTargetId, bool EnteredCombat);
public sealed record CombatSwing(
    string AttackerId, string TargetId, int Damage, int FinalHealth, int MaxHealth, bool Killed,
    StrikeResult Result = StrikeResult.Hit, bool Ranged = false, bool Spared = false, bool OffHand = false,
    bool OtherPlayer = false, int Absorbed = 0, uint SpellId = 0);
public enum StrikeResult
{
    Hit,
    Miss,
    Dodge,
    Parry,
    Block,
    Critical,

    Evade,
}

public sealed record PlayerRegen(int FinalHealth, int MaxHealth);
public sealed record PlayerHealResult(int Applied, int FinalHealth, int MaxHealth);

public sealed record RangedAttackContext(
    bool InMeleeRange,
    double Distance,
    bool HasRangedWeapon,
    TimeSpan RangedInterval,
    bool IsMoving);

public sealed record StanceChange(
    string AttackerId, string TargetId, CombatService.AttackStance From, CombatService.AttackStance To);

public sealed record RangedShot(
    string AttackerId, string TargetId, int SpellVisualId, double Speed, TimeSpan FlightTime);

public sealed record CreatureCombatStats(
    int MaxHealth, int MinDamage, int MaxDamage, TimeSpan SwingInterval, bool IsTrainingDummy = false,
    bool IgnoresHealthLoss = false);

public sealed partial class CombatService
{
    public const int CreatureMaxHealth = 100;
    public const int PlayerMaxHealth = 100;
    public const int SwingDamage = 12;
    public const int RetaliationDamage = 5;
    public const int RegenPerInterval = 2;
    public static readonly IReadOnlyDictionary<int, (double PerStamina, int Plus)> RegenByClass =
        new Dictionary<int, (double PerStamina, int Plus)>
        {
            [PlayerClassIds.Warrior] = (2.0, 9),
            [PlayerClassIds.Mage] = (1.2, 6),
            [PlayerClassIds.Ranger] = (1.8, 2),
            [PlayerClassIds.Monk] = (1.5, 5),
        };

    public static int RegenAmount(int classId, int totalStamina, int healthRegenRating) =>
        (RegenByClass.TryGetValue(classId, out var rate)
            ? (int)(Math.Max(0, totalStamina) * rate.PerStamina + rate.Plus)
            : RegenPerInterval)
        + Math.Max(0, healthRegenRating) * HealthRegenPerRating;
    public const int HealthRegenPerRating = 1;
    public const double ArmorMitigationPerPoint = 0.001;
    public const double MaxArmorMitigation = 0.75;
    public const double MeleeDamagePerStrength = 0.1;
    public const double MeleeDamagePerAttackPower = 0.05;
    public const double HealingPerHealingPower = 0.5;
    public const double SpellDamagePerSpellPower = 0.1;
    public const int HealthPerStamina = 10;
    public const double DodgeChancePerAgility = 0.001;
    public const double MaxDodgeChance = 0.5;
    public const double DodgeChancePerDodgeRating = 0.001;
    public const double CritChancePerRating = 0.001;
    public const double MaxCritChance = 0.3;
    public const double CriticalDamageMultiplier = 2.0;
    public const double MeleeMissChance = 0.05;
    public const double MissChancePerHitRating = 0.001;
    public const double ParryChancePerRating = 0.001;
    public const double MaxParryChance = 0.3;
    public const double BlockChancePerRating = 0.001;
    public const double MaxBlockChance = 0.3;
    public const double BlockDamageReduction = 0.5;
    public static readonly TimeSpan SwingInterval = TimeSpan.FromSeconds(2);
    public static TimeSpan EffectiveSwingInterval(int meleeAttackSpeedPercent) =>
        meleeAttackSpeedPercent == 0
            ? SwingInterval
            : SwingInterval / Math.Max(0.01, 1 + meleeAttackSpeedPercent / 100.0);
    public static readonly TimeSpan RegenInterval = TimeSpan.FromSeconds(8);

    public enum AttackStance
    {
        None,
        Melee,
        Ranged,
    }

    public const double MaxRangedAttackDistance = 25.0;

    public const double RangedProjectileSpeed = 30.0;

    public const int MaxRangedProjectilesInFlight = 4;

    public const int RangedAttackSpellVisualId = 81;

    public static readonly TimeSpan RangedDrawTime = TimeSpan.FromMilliseconds(750);

    private sealed record RangedProjectile(
        string AttackerId, string TargetId, DateTimeOffset ArrivesAt,
        int DamagePercent = 100, int DamageBonus = 0, uint SpellId = 0);

    private readonly Func<int, int, int> _rollDamage;
    private readonly Func<double> _rollUnit;
    private readonly Dictionary<string, string> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nextSwings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _health = new(StringComparer.Ordinal);

    public double HealthFractionOf(string unitId)
    {
        var max = MaxHealthFor(unitId);
        if (max <= 0) return 1.0;
        var health = _health.TryGetValue(unitId, out var current) ? current : max;
        return Math.Clamp((double)health / max, 0.0, 1.0);
    }

    public (int Applied, int FinalHealth, int MaxHealth) RestoreHealth(string unitId, int amount)
    {
        var max = MaxHealthFor(unitId);
        var health = _health.TryGetValue(unitId, out var current) ? current : max;
        if (_dead.Contains(unitId) || health <= 0 || amount <= 0) return (0, health, max);
        var final = Math.Min(max, health + amount);
        _health[unitId] = final;
        return (final - health, final, max);
    }

    private readonly HashSet<string> _dead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _retaliation = new(StringComparer.Ordinal);
    private IHostilityLog _hostility;
    public const double ThreatSwitchMargin = 0.3;

    public void UseHostilityLog(IHostilityLog log) => _hostility = log;
    private readonly Dictionary<string, CreatureCombatStats> _stats = new(StringComparer.Ordinal);
    private Func<string, CreatureCombatStats?>? _resolveStats;
    private readonly List<AttackStartResult> _pendingCombatEntries = [];
    private readonly Dictionary<string, bool> _meleeInReach = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _shotReadyAt = new(StringComparer.Ordinal);
    private readonly List<RangedProjectile> _projectiles = [];
    private int _playerClassId;
    private int _playerLevel = 1;
    private StartingAttributes _levelOneAttributes = new(0, 0, 0, 0);
    private int _playerHealth = PlayerMaxHealth;
    private bool _playerStruck;

    public bool TakePlayerStruck()
    {
        var struck = _playerStruck;
        _playerStruck = false;
        return struck;
    }

    public Func<string, bool>? SparedBy { get; set; }

    public Func<int, DateTimeOffset, int>? AbsorbIncoming { get; set; }

    public Func<string, bool>? Evading { get; set; }

    private bool IsEvading(string unitId) => !LooksLikePlayerId(unitId) && Evading?.Invoke(unitId) == true;

    private static CombatSwing Rolled(CombatSwing swing, StrikeResult rolled) =>
        swing.Result == StrikeResult.Evade ? swing : swing with { Result = rolled };

    public Func<string, DateTimeOffset, bool>? CannotAct { get; set; }

    public Func<bool>? InActiveDuel { get; set; }
    private bool _playerDead;
    private DateTimeOffset _nextRegen;

    public void EnterAsCharacter(
        int classId, int level, string? playerId = null, int? health = null, StartingAttributes? levelOneAttributes = null)
    {
        _playerClassId = classId;
        _playerLevel = level;
        if (levelOneAttributes is not null) _levelOneAttributes = levelOneAttributes;
        var max = EffectiveMaxHealth(0);
        _playerHealth = health is int carried ? Math.Clamp(carried, 1, max) : max;
        _selfPlayerId = playerId;
    }

    private string? _selfPlayerId;

    private bool IsSelf(string playerId) =>
        _selfPlayerId is null || string.Equals(_selfPlayerId, playerId, StringComparison.Ordinal);

    public void UseTargetStats(Func<string, CreatureCombatStats?> resolve) => _resolveStats = resolve;

    public IReadOnlyList<AttackStartResult> DrainCombatEntries()
    {
        if (_pendingCombatEntries.Count == 0) return [];
        var drained = _pendingCombatEntries.ToArray();
        _pendingCombatEntries.Clear();
        return drained;
    }

    public CombatService(Func<int, int, int>? rollDamage = null, Func<double>? rollUnit = null, TimeProvider? time = null)
    {
        _hostility = new LocalHostilityLog(time);
        _rollDamage = rollDamage ?? ((min, max) => Random.Shared.Next(min, max + 1));
        _rollUnit = rollUnit ?? (() => Random.Shared.NextDouble());
    }

    public AttackStartResult? Start(
        string attackerId,
        string targetId,
        IReadOnlySet<string> visibleTargets,
        DateTimeOffset now,
        CreatureCombatStats? targetStats = null,
        Func<string, string, bool>? isDuelOpponent = null,
        Func<string, string, bool>? isPvpFlagged = null,
        bool holdFireUntilStruck = false)
    {
        if (_playerDead || !visibleTargets.Contains(targetId) || _dead.Contains(targetId)) return null;
        if (IsEvading(targetId)) return null;
        if (LooksLikePlayerId(targetId)
            && !(isDuelOpponent?.Invoke(attackerId, targetId) ?? false)
            && !(isPvpFlagged?.Invoke(attackerId, targetId) ?? false))
            return null;
        if (targetStats is not null) _stats[targetId] = targetStats;
        var retaliationInterval = SwingIntervalFor(targetId);
        var dummy = IsTrainingDummyTarget(targetId);
        if (_targets.TryGetValue(attackerId, out var current))
        {
            if (string.Equals(current, targetId, StringComparison.Ordinal))
            {
                if (holdFireUntilStruck || !_holdingFire.Contains(attackerId)) return null;
                OpenFire(attackerId, now);
                return new AttackStartResult(attackerId, targetId, null, false);
            }
            _holdingFire.Remove(attackerId);
            bool leftSwinging = _retaliation.ContainsKey(current);
            _targets[attackerId] = targetId;
            _nextSwings[attackerId] = now;
            BeginInMelee(attackerId);
            if (SwingsBack(targetId, dummy)) _retaliation[targetId] = now + retaliationInterval;
            if (!LooksLikePlayerId(targetId)) _hostility.AddFighter(targetId, attackerId);
            if (!LooksLikePlayerId(current))
            {
                if (!leftSwinging) _hostility.RemoveFighter(current, attackerId);
                else if (_hostility.Victim(current) is null) _hostility.SetVictim(current, attackerId);
            }
            _nextRegen = now + RegenInterval;
            return new AttackStartResult(attackerId, targetId, current, false);
        }

        _targets[attackerId] = targetId;
        _nextSwings[attackerId] = now;
        if (holdFireUntilStruck) _holdingFire.Add(attackerId);
        else _holdingFire.Remove(attackerId);
        BeginInMelee(attackerId);
        if (SwingsBack(targetId, dummy)) _retaliation[targetId] = now + retaliationInterval;
        if (!LooksLikePlayerId(targetId)) _hostility.AddFighter(targetId, attackerId);
        _nextRegen = now + RegenInterval;
        return new AttackStartResult(attackerId, targetId, null, true);
    }

    // A creature that notices a player opens the fight; the player only swings back once struck.
    private readonly HashSet<string> _holdingFire = new(StringComparer.Ordinal);

    private void OpenFire(string playerId, DateTimeOffset now)
    {
        if (!_holdingFire.Remove(playerId) || !_targets.TryGetValue(playerId, out var targetId)) return;
        _nextSwings[playerId] = now;
        _pendingCombatEntries.Add(new AttackStartResult(playerId, targetId, null, false));
    }

    private static bool SwingsBack(string targetId, bool trainingDummy) =>
        !trainingDummy && !LooksLikePlayerId(targetId);

    public IReadOnlyList<CombatSwing> Tick(
        DateTimeOffset now, int playerArmor = 0, int playerStrength = 0, int playerStamina = 0,
        int playerAgility = 0, int playerCritRating = 0, int playerParryRating = 0, int playerBlockRating = 0,
        int playerHitRating = 0, int playerMeleeAttackSpeedPercent = 0, int playerDodgeRating = 0)
    {
        var swings = new List<CombatSwing>();
        foreach (var projectile in _projectiles.Where(shot => now >= shot.ArrivesAt).ToArray())
        {
            _projectiles.Remove(projectile);
            if (_dead.Contains(projectile.TargetId)) continue;
            var shotRoll = RollOutgoingSwing(playerHitRating, playerCritRating, playerStrength, playerAgility);
            var shotDamage = shotRoll.Result == StrikeResult.Miss
                ? 0
                : ApplyWeaponDamageModifiers(shotRoll.Damage, projectile.DamagePercent, projectile.DamageBonus);
            if (OverwhelmingBlows && LooksLikePlayerId(projectile.AttackerId) && shotRoll.Result != StrikeResult.Miss)
                shotDamage = GodModeDamage;
            swings.Add(Rolled(DamageTarget(projectile.AttackerId, projectile.TargetId, shotDamage, now), shotRoll.Result)
                with
            { Ranged = true, SpellId = projectile.SpellId });
        }

        foreach (var (attackerId, targetId) in _targets.ToArray())
        {
            if (StanceOf(attackerId) != AttackStance.Melee) continue;
            if (!_meleeInReach.GetValueOrDefault(attackerId, true)) continue;
            if (!_nextSwings.TryGetValue(attackerId, out var due) || now < due) continue;
            if (_holdingFire.Contains(attackerId)) continue;
            if (!LooksLikePlayerId(attackerId) && CannotAct?.Invoke(attackerId, now) == true) continue;
            (int Damage, StrikeResult Result) roll;
            if (LooksLikePlayerId(attackerId))
            {
                var interval = EffectiveSwingInterval(playerMeleeAttackSpeedPercent);
                _nextSwings[attackerId] = now + interval;
                if (_playerOffHandWeapon is not null) _offHandSwings[attackerId] = now + interval * OffHandDelayFraction;
                var outgoing = RollOutgoingSwing(playerHitRating, playerCritRating, playerStrength, playerAgility);
                roll = (OverwhelmingBlows && outgoing.Result != StrikeResult.Miss ? GodModeDamage : outgoing.Damage, outgoing.Result);
            }
            else if (_rollUnit() < MeleeMissChance)
            {
                _nextSwings[attackerId] = now + SwingIntervalFor(attackerId);
                roll = (0, StrikeResult.Miss);
            }
            else
            {
                _nextSwings[attackerId] = now + SwingIntervalFor(attackerId);
                roll = (DamageFor(attackerId), StrikeResult.Hit);
            }
            swings.Add(Rolled(DamageTarget(attackerId, targetId, roll.Damage, now), roll.Result));
        }

        foreach (var (attackerId, due) in _offHandSwings.ToArray())
        {
            if (now < due) continue;
            _offHandSwings.Remove(attackerId);
            if (_playerOffHandWeapon is not { } offHand) continue;
            if (!_targets.TryGetValue(attackerId, out var targetId) || _dead.Contains(targetId)) continue;
            if (StanceOf(attackerId) != AttackStance.Melee) continue;
            if (!_meleeInReach.GetValueOrDefault(attackerId, true)) continue;
            var outgoing = RollOutgoingSwing(playerHitRating, playerCritRating, playerStrength, playerAgility, offHand);
            var damage = OverwhelmingBlows && outgoing.Result != StrikeResult.Miss ? GodModeDamage : outgoing.Damage;
            swings.Add(Rolled(DamageTarget(attackerId, targetId, damage, now), outgoing.Result) with { OffHand = true });
        }

        foreach (var (creatureId, due) in _retaliation.ToArray())
        {
            if (_playerDead || now < due) continue;
            if (IsEvading(creatureId))
            {
                _retaliation.Remove(creatureId);
                continue;
            }
            if (CannotAct?.Invoke(creatureId, now) == true) continue;
            string? playerId = null;
            if (!LooksLikePlayerId(creatureId))
            {
                var victim = RetaliationTarget(creatureId);
                if (victim is null)
                {
                    _retaliation.Remove(creatureId);
                    continue;
                }
                if (!LooksLikePlayerId(victim))
                {
                    if (!_meleeInReach.GetValueOrDefault(victim, true)) continue;
                    _retaliation[creatureId] = now + SwingIntervalFor(creatureId);
                    var retaliation = DamageTarget(creatureId, victim, DamageFor(creatureId), now);
                    swings.Add(Rolled(retaliation, StrikeResult.Hit));
                    continue;
                }
                if (IsSelf(victim)) playerId = victim;
            }
            playerId ??= _targets.FirstOrDefault(pair =>
                string.Equals(pair.Value, creatureId, StringComparison.Ordinal)).Key;
            if (playerId is null)
            {
                _retaliation.Remove(creatureId);
                continue;
            }

            var reach = _meleeInReach.TryGetValue(creatureId, out var ownReach)
                ? ownReach
                : _meleeInReach.GetValueOrDefault(playerId, true);
            if (!reach) continue;

            _retaliation[creatureId] = now + SwingIntervalFor(creatureId);
            OpenFire(playerId, now);
            bool dodged = _rollUnit() < DodgeChance(playerAgility, playerDodgeRating);
            bool parried = !dodged && _rollUnit() < ParryChance(playerParryRating);
            bool blocked = !dodged && !parried && _rollUnit() < BlockChance(playerBlockRating);
            var rawRetaliation = DamageFor(creatureId);

            StrikeResult result;
            int damage;
            if (dodged)
            {
                result = StrikeResult.Dodge;
                damage = 0;
            }
            else if (parried)
            {
                result = StrikeResult.Parry;
                damage = 0;
            }
            else if (blocked)
            {
                result = StrikeResult.Block;
                damage = ApplyArmorMitigation(
                    (int)Math.Round(rawRetaliation * (1 - BlockDamageReduction), MidpointRounding.AwayFromZero), playerArmor);
            }
            else
            {
                result = StrikeResult.Hit;
                damage = ApplyArmorMitigation(rawRetaliation, playerArmor);
            }
            if (GodMode)
            {
                damage = 0;
                result = StrikeResult.Hit;
            }
            if (damage > 0) _playerStruck = true;
            var absorbed = 0;
            if (AbsorbIncoming is not null)
            {
                var through = AbsorbIncoming(damage, now);
                absorbed = damage - through;
                damage = through;
            }

            var final = Math.Max(0, _playerHealth - damage);
            var spared = final == 0 && SparedBy?.Invoke(creatureId) == true;
            if (spared) final = 1;
            _playerHealth = final;
            var killed = final == 0;
            if (killed)
            {
                _playerDead = true;
                _targets.Clear();
                _nextSwings.Clear();
                _retaliation.Clear();
                _meleeInReach.Clear();
                _shotReadyAt.Clear();
                _projectiles.Clear();
            }

            swings.Add(new CombatSwing(
                creatureId, playerId, damage, final, EffectiveMaxHealth(playerStamina), killed, result, Spared: spared,
                Absorbed: absorbed));
        }

        return swings;
    }

    public const int GodModeDamage = 99999;

    public bool GodMode { get; set; }

    public bool OverwhelmingBlows { get; set; }
    private (int MinDamage, int MaxDamage)? _playerWeapon;

    public void SetPlayerWeapon(int? minDamage, int? maxDamage) =>
        _playerWeapon = minDamage is int low && maxDamage is int high && high >= low && low > 0
            ? (low, high)
            : null;

    public void SetPlayerOffHandWeapon(int? minDamage, int? maxDamage) =>
        _playerOffHandWeapon = minDamage is int low && maxDamage is int high && high >= low && low > 0
            ? (low, high)
            : null;

    public const double OffHandDelayFraction = 0.4;

    private (int MinDamage, int MaxDamage)? _playerOffHandWeapon;
    private readonly Dictionary<string, DateTimeOffset> _offHandSwings = new(StringComparer.Ordinal);

    private (int Damage, StrikeResult Result) RollOutgoingSwing(
        int hitRating, int critRating, int strength, int agility, (int MinDamage, int MaxDamage)? weapon = null)
    {
        if (_rollUnit() < MissChance(hitRating)) return (0, StrikeResult.Miss);
        bool critical = _rollUnit() < CritChance(critRating);
        int attackPower = OwnAttackPower(strength, agility);
        int weaponDamage = (weapon ?? _playerWeapon) is { } held
            ? Roll(held.MinDamage, held.MaxDamage)
            : SwingDamage;
        int rawDamage = ApplyAttackPowerBonus(ApplyStrengthBonus(weaponDamage, strength), attackPower);
        int damage = critical
            ? (int)Math.Round(rawDamage * CriticalDamageMultiplier, MidpointRounding.AwayFromZero)
            : rawDamage;
        return (damage, critical ? StrikeResult.Critical : StrikeResult.Hit);
    }

    public void EnlistAttacker(string creatureId, string targetId, DateTimeOffset now)
    {
        if (_dead.Contains(creatureId) || _dead.Contains(targetId)) return;
        if (LooksLikePlayerId(creatureId) || IsTrainingDummyTarget(creatureId) || IsEvading(creatureId)) return;
        _retaliation[creatureId] = now + SwingIntervalFor(creatureId);
        _hostility.AddFighter(creatureId, targetId);
        _hostility.SetVictim(creatureId, targetId);
    }

    public void SetInMeleeReach(string attackerId, bool inReach) => _meleeInReach[attackerId] = inReach;

    public void ReleaseJoiner(string creatureId)
    {
        _retaliation.Remove(creatureId);
        _meleeInReach.Remove(creatureId);
    }

    public CombatSwing DamageTarget(
        string sourceId, string targetId, int damage, DateTimeOffset now, int playerStaminaBonus = 0)
    {
        bool isSelf = LooksLikePlayerId(targetId) && IsSelf(targetId);
        if (LooksLikePlayerId(targetId) && !isSelf)
        {
            if (_dead.Contains(targetId)) return new CombatSwing(sourceId, targetId, 0, 0, 0, false, OtherPlayer: true);
            EnterCombatOnDamage(sourceId, targetId, now);
            return new CombatSwing(sourceId, targetId, damage, 0, 0, false, OtherPlayer: true);
        }
        var maxHealth = isSelf ? EffectiveMaxHealth(playerStaminaBonus) : MaxHealthFor(targetId);
        var health = isSelf
            ? _playerHealth
            : _health.TryGetValue(targetId, out var current) ? current : maxHealth;

        if (BlowFindsNothing(sourceId, targetId, health, maxHealth) is { } nothing) return nothing;
        if (isSelf) OpenFire(targetId, now);
        EnterCombatOnDamage(sourceId, targetId, now);
        (damage, var absorbed) = DamageThatLands(sourceId, targetId, isSelf, damage, now);
        if (IgnoresHealthLoss(targetId))
            return new CombatSwing(sourceId, targetId, damage, health, maxHealth, false, Absorbed: absorbed);
        var final = Math.Max(0, health - damage);
        var spared = isSelf && final == 0 && SparedBy?.Invoke(sourceId) == true;
        if (spared) final = 1;
        if (isSelf)
        {
            _playerHealth = final;
            if (damage > 0) _nextRegen = now + RegenInterval;
        }
        else _health[targetId] = final;
        if (damage > 0 && !LooksLikePlayerId(targetId))
            _hostility.Add(targetId, sourceId, damage);
        var killed = final == 0;
        if (killed) SettleDeath(sourceId, targetId, now);

        return new CombatSwing(sourceId, targetId, damage, final, maxHealth, killed, Spared: spared, Absorbed: absorbed);
    }

    private void SettleDeath(string sourceId, string targetId, DateTimeOffset now)
    {
        _dead.Add(targetId);
        _unitShields.Remove(targetId);
        _hostility.ClearFighters(targetId);
        if (_targets.TryGetValue(sourceId, out var currentTarget)
            && string.Equals(currentTarget, targetId, StringComparison.Ordinal))
        {
            _targets.Remove(sourceId);
            _nextSwings.Remove(sourceId);
            StopAttacking(sourceId);
        }

        if (LooksLikePlayerId(targetId))
        {
            _playerDead = true;
            _targets.Clear();
            _nextSwings.Clear();
            _retaliation.Clear();
        }

        _retaliation.Remove(targetId);
        _nextRegen = now + RegenInterval;
    }

    private CombatSwing? BlowFindsNothing(string sourceId, string targetId, int health, int maxHealth)
    {
        bool down = _dead.Contains(targetId) || health <= 0;
        if (down) return new CombatSwing(sourceId, targetId, 0, 0, maxHealth, false);
        return IsEvading(targetId)
            ? new CombatSwing(sourceId, targetId, 0, health, maxHealth, false, StrikeResult.Evade)
            : null;
    }

    private (int Damage, int Absorbed) DamageThatLands(
        string sourceId, string targetId, bool isSelf, int damage, DateTimeOffset now)
    {
        if (OverwhelmingBlows && LooksLikePlayerId(sourceId) && !LooksLikePlayerId(targetId)) damage = GodModeDamage;
        if (GodMode && LooksLikePlayerId(targetId)) damage = 0;
        if (!isSelf) return ThroughUnitShield(targetId, damage);

        if (damage > 0) _playerStruck = true;
        if (AbsorbIncoming is null) return (damage, 0);
        var through = AbsorbIncoming(damage, now);
        return (through, damage - through);
    }

    private readonly Dictionary<string, (int Percent, int Left)> _unitShields = new(StringComparer.Ordinal);

    public void ShieldUnit(string unitId, int percent, int amount) => _unitShields[unitId] = (percent, amount);

    public bool UnshieldUnit(string unitId) => _unitShields.Remove(unitId);

    private (int Damage, int Absorbed) ThroughUnitShield(string unitId, int damage)
    {
        if (damage <= 0 || !_unitShields.TryGetValue(unitId, out var shield)) return (damage, 0);
        var absorbed = Math.Min(shield.Left, damage * shield.Percent / 100);
        if (absorbed >= shield.Left) _unitShields.Remove(unitId);
        else _unitShields[unitId] = shield with { Left = shield.Left - absorbed };
        return (damage - absorbed, absorbed);
    }

    public void EnterCombatOnCast(string sourceId, string targetId, DateTimeOffset now) =>
        EnterCombatOnDamage(sourceId, targetId, now);

    private bool OpensAFight(string sourceId, string targetId)
    {
        bool ownPlayerStriking = LooksLikePlayerId(sourceId) && IsSelf(sourceId) && !_playerDead;
        bool alreadyFighting = _targets.ContainsKey(sourceId);
        bool fairTarget = !string.Equals(sourceId, targetId, StringComparison.Ordinal)
            && !_dead.Contains(targetId)
            && !IsEvading(targetId);
        return ownPlayerStriking && !alreadyFighting && fairTarget;
    }

    private void EnterCombatOnDamage(string sourceId, string targetId, DateTimeOffset now)
    {
        if (!OpensAFight(sourceId, targetId)) return;
        _targets[sourceId] = targetId;
        if (!LooksLikePlayerId(targetId)) _hostility.AddFighter(targetId, sourceId);
        _nextSwings[sourceId] = now + SwingInterval;
        BeginInMelee(sourceId);
        if (SwingsBack(targetId, IsTrainingDummyTarget(targetId))) _retaliation[targetId] = now + SwingIntervalFor(targetId);
        _nextRegen = now + RegenInterval;
        _pendingCombatEntries.Add(new AttackStartResult(sourceId, targetId, null, true));
    }

}
