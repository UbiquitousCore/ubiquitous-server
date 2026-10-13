namespace Ubiquitous.Game;

public sealed partial class CombatService
{
    public AttackStance StanceOf(string attackerId) =>
        !_targets.ContainsKey(attackerId) ? AttackStance.None
            : _shotReadyAt.ContainsKey(attackerId) ? AttackStance.Ranged
            : AttackStance.Melee;

    public StanceChange? UpdateStance(string attackerId, RangedAttackContext context, DateTimeOffset now)
    {
        if (!_targets.TryGetValue(attackerId, out var targetId)) return null;
        _meleeInReach[attackerId] = context.InMeleeRange;
        var wasRanged = _shotReadyAt.ContainsKey(attackerId);
        var wantsRanged = context.HasRangedWeapon && !context.InMeleeRange;
        if (wasRanged == wantsRanged) return null;

        if (wantsRanged)
        {
            _shotReadyAt[attackerId] = now + context.RangedInterval;
            return new StanceChange(attackerId, targetId, AttackStance.Melee, AttackStance.Ranged);
        }

        _shotReadyAt.Remove(attackerId);
        _nextSwings[attackerId] = now;
        return new StanceChange(attackerId, targetId, AttackStance.Ranged, AttackStance.Melee);
    }

    public RangedShot? TickRangedShot(string attackerId, RangedAttackContext context, DateTimeOffset now)
    {
        if (!_shotReadyAt.TryGetValue(attackerId, out var readyAt)) return null;
        if (!_targets.TryGetValue(attackerId, out var targetId)) return null;
        if (_dead.Contains(targetId) || _playerDead) return null;

        if (context.IsMoving)
        {
            var draw = context.RangedInterval < RangedDrawTime ? context.RangedInterval : RangedDrawTime;
            if (readyAt < now + draw) _shotReadyAt[attackerId] = now + draw;
            return null;
        }

        var inBand = !context.InMeleeRange && context.Distance <= MaxRangedAttackDistance;
        if (!inBand || now < readyAt) return null;
        int inFlight = _projectiles.Count(projectile =>
            string.Equals(projectile.AttackerId, attackerId, StringComparison.Ordinal));
        if (inFlight >= MaxRangedProjectilesInFlight) return null;

        _shotReadyAt[attackerId] = now + context.RangedInterval;
        var flight = TimeSpan.FromSeconds(Math.Max(0, context.Distance) / RangedProjectileSpeed);
        _projectiles.Add(new RangedProjectile(attackerId, targetId, now + flight));
        return new RangedShot(attackerId, targetId, RangedAttackSpellVisualId, RangedProjectileSpeed, flight);
    }

    public RangedShot LooseSpellShot(
        string attackerId, string targetId, double distance, DateTimeOffset now,
        int spellVisualId = RangedAttackSpellVisualId, int damagePercent = 100, int damageBonus = 0, uint spellId = 0)
    {
        var flight = TimeSpan.FromSeconds(Math.Max(0, distance) / RangedProjectileSpeed);
        _projectiles.Add(new RangedProjectile(
            attackerId, targetId, now + flight, damagePercent, damageBonus, spellId));
        return new RangedShot(attackerId, targetId, spellVisualId, RangedProjectileSpeed, flight);
    }

    public CombatSwing StrikeSpellBlow(
        string attackerId, string targetId, DateTimeOffset now,
        int damagePercent = 100, int damageBonus = 0,
        int hitRating = 0, int critRating = 0, int strength = 0, int agility = 0)
    {
        var roll = RollOutgoingSwing(hitRating, critRating, strength, agility);
        var damage = roll.Result == StrikeResult.Miss
            ? 0
            : ApplyWeaponDamageModifiers(roll.Damage, damagePercent, damageBonus);
        return Rolled(DamageTarget(attackerId, targetId, damage, now), roll.Result);
    }

    public static int ApplyWeaponDamageModifiers(int damage, int percent, int bonus)
    {
        var scaled = percent is 100 or 0
            ? damage
            : (int)Math.Round(damage * (percent / 100.0), MidpointRounding.AwayFromZero);
        return Math.Max(0, scaled + bonus);
    }

    private void StopAttacking(string attackerId)
    {
        _meleeInReach.Remove(attackerId);
        _shotReadyAt.Remove(attackerId);
    }

    private void BeginInMelee(string attackerId)
    {
        _meleeInReach.Remove(attackerId);
        _shotReadyAt.Remove(attackerId);
    }
}
