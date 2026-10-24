namespace Ubiquitous.Game;

public sealed partial class CombatService
{
    public PlayerHealResult? HealPlayer(int amount, int staminaBonus = 0)
    {
        if (_playerDead) return null;
        var max = EffectiveMaxHealth(staminaBonus);
        var before = _playerHealth;
        _playerHealth = Math.Min(max, _playerHealth + Math.Max(0, amount));
        return new PlayerHealResult(_playerHealth - before, _playerHealth, max);
    }

    public PlayerHealResult? Revive(int staminaBonus = 0)
    {
        if (!_playerDead) return null;
        _playerDead = false;
        var max = EffectiveMaxHealth(staminaBonus);
        _playerHealth = max;
        return new PlayerHealResult(max, max, max);
    }

    public PlayerHealResult? ReviveTo(int amount, int staminaBonus = 0)
    {
        if (!_playerDead) return null;
        _playerDead = false;
        var max = EffectiveMaxHealth(staminaBonus);
        _playerHealth = Math.Clamp(amount, 1, max);
        return new PlayerHealResult(_playerHealth, _playerHealth, max);
    }

    public int PlayerHealth => _playerHealth;

    public PlayerRegen? TickRegen(DateTimeOffset now, int staminaBonus = 0, int healthRegenRating = 0)
    {
        var max = EffectiveMaxHealth(staminaBonus);
        if (_playerDead || _playerHealth >= max) return null;
        if (PlayerIsFighting() || InActiveDuel?.Invoke() == true) return null;
        if (now < _nextRegen) return null;
        _nextRegen = now + RegenInterval;
        var amount = RegenAmount(_playerClassId, OwnAttributes.Stamina + staminaBonus, healthRegenRating);
        _playerHealth = Math.Min(max, _playerHealth + amount);
        return new PlayerRegen(_playerHealth, max);
    }

    public bool PlayerInCombat => PlayerIsFighting() || InActiveDuel?.Invoke() == true;

    private bool PlayerIsFighting()
    {
        foreach (var (attackerId, targetId) in _targets)
        {
            if (LooksLikePlayerId(attackerId) && IsSelf(attackerId)) return true;
            if (LooksLikePlayerId(targetId) && IsSelf(targetId)) return true;
        }

        foreach (var targetId in _retaliation.Keys)
        {
            if (LooksLikePlayerId(targetId) && IsSelf(targetId)) return true;
        }

        return false;
    }
}
