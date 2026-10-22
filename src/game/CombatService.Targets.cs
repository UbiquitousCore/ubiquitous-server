namespace Ubiquitous.Game;

public sealed partial class CombatService
{
    public string? Disengage(string attackerId)
    {
        if (!_targets.TryGetValue(attackerId, out var targetId)) return null;
        _targets.Remove(attackerId);
        _hostility.RemoveFighter(targetId, attackerId);
        _nextSwings.Remove(attackerId);
        _holdingFire.Remove(attackerId);
        _retaliation.Remove(targetId);
        StopAttacking(attackerId);
        return targetId;
    }

    public string? CurrentTargetOf(string attackerId) =>
        _targets.TryGetValue(attackerId, out var targetId) ? targetId : null;

    public bool IsDead(string targetId) => _dead.Contains(targetId);

    public void MarkDead(string targetId) => _dead.Add(targetId);

    public void ClearDeath(string targetId)
    {
        _dead.Remove(targetId);
        _health.Remove(targetId);
    }

    public void ClearHostility(string mobId) => _hostility.Clear(mobId);

    public int? ResetHealth(string unitId)
    {
        if (_dead.Contains(unitId)) return null;
        _health.Remove(unitId);
        return MaxHealthFor(unitId);
    }

    public string? RetaliationTarget(string mobId)
    {
        string? Fallback()
        {
            foreach (var pair in _targets)
            {
                if (string.Equals(pair.Value, mobId, StringComparison.Ordinal) && !_dead.Contains(pair.Key))
                    return pair.Key;
            }
            return null;
        }

        bool Eligible(string attacker)
        {
            if (_dead.Contains(attacker)) return false;
            if (_targets.TryGetValue(attacker, out var fighting)
                && string.Equals(fighting, mobId, StringComparison.Ordinal))
                return true;
            return _hostility.Fighters(mobId).Contains(attacker);
        }

        var current = _hostility.Victim(mobId);
        if (current is not null && !Eligible(current))
        {
            current = null;
            _hostility.ClearVictim(mobId);
        }

        string? best = null;
        var bestValue = 0.0;
        foreach (var (attacker, value) in _hostility.For(mobId))
        {
            if (!Eligible(attacker)) continue;
            if (value > bestValue) { bestValue = value; best = attacker; }
        }

        if (best is null) return current ?? Fallback();
        var currentValue = current is not null && _hostility.For(mobId).TryGetValue(current, out var held)
            ? held
            : 0;
        if (current is null || bestValue > currentValue * (1 + ThreatSwitchMargin))
            _hostility.SetVictim(mobId, best);
        return _hostility.Victim(mobId) ?? best;
    }

    public bool IsPlayerDead => _playerDead;

    private CreatureCombatStats? StatsFor(string targetId)
    {
        if (_stats.TryGetValue(targetId, out var known)) return known;
        if (_resolveStats?.Invoke(targetId) is not { } resolved) return null;
        _stats[targetId] = resolved;
        return resolved;
    }

    private int MaxHealthFor(string targetId) => StatsFor(targetId)?.MaxHealth ?? CreatureMaxHealth;

    private TimeSpan SwingIntervalFor(string targetId) => StatsFor(targetId)?.SwingInterval ?? SwingInterval;

    private bool IsTrainingDummyTarget(string targetId) => StatsFor(targetId)?.IsTrainingDummy ?? false;

    private bool IgnoresHealthLoss(string targetId) => StatsFor(targetId)?.IgnoresHealthLoss ?? false;

    private int DamageFor(string targetId) =>
        StatsFor(targetId) is { } stats ? Roll(stats.MinDamage, stats.MaxDamage) : RetaliationDamage;

    public int RollAmount(int min, int max) => Roll(min, max);

    public int PickOne(int count) => count <= 1 ? 0 : Math.Min(count - 1, (int)(_rollUnit() * count));

    private int Roll(int min, int max) => min >= max ? min : _rollDamage(min, max);

    private static bool LooksLikePlayerId(string id) => id.StartsWith("player:", StringComparison.Ordinal);
}
