using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private IReadOnlyList<string> GrantKillXp(CreatureDefinition? creature, DateTimeOffset now)
    {
        if (creature is null || _character is null) return [];
        return GrantXp(LevelingService.KillXp(_level, creature.Level), now);
    }

    private IReadOnlyList<string> GrantXp(int gained, DateTimeOffset now)
    {
        if (_character is null || gained <= 0 || _level >= LevelingService.MaxLevel) return [];

        var result = LevelingService.ApplyXp(_level, _xp, gained);
        _xp = result.Xp;
        var values = new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Xp] = _xp,
            [WireCodes.UnitValue.XpToNextLevel] = LevelingService.XpToNextLevel(result.Level),
        };

        if (result.LevelsGained > 0) ApplyLevel(result.Level, _character.Health, values, now);

        var responses = new List<string>
        {
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(values)),
            Reply(WebMessageTypes.PlayerExpGained, new PlayerExpGainedPayload(gained, result.LevelsGained > 0)),
        };
        if (result.LevelsGained > 0 && _level >= TalentBook.FirstPointLevel) responses.Add(TalentStateReply());
        return responses;
    }

    private IReadOnlyList<string> ApplyDeathPenalty(string killerId)
    {
        var killedByPlayer = killerId.StartsWith("player:", StringComparison.Ordinal);
        var lost = DeathPenaltyService.GoldLost(_level, _belongings.Currency, killedByPlayer);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.PlayerDeath, new PlayerDeathPayload(
                WebNetworkId.FromString(killerId), lost)),
        };
        if (lost <= 0) return responses;

        _belongings.Currency -= lost;
        responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
            new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })));
        return responses;
    }

    // Everything that follows from the level changing, and what to tell the client of it.
    private void ApplyLevel(int level, int? health, Dictionary<string, int> values, DateTimeOffset now)
    {
        _level = level;
        _character = _character! with { Level = _level };
        _combat.EnterAsCharacter(_character.Class, _level, PlayerUnitId, health);
        RefreshWornAttributes();
        _spells.EnterAsCharacter(_character.Class, _level);
        var staminaBonus = _attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Stamina, now);
        var wisdomBonus = _attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Wisdom, now);
        values[WireCodes.UnitValue.Level] = _level;
        values[WireCodes.UnitValue.Health] = _combat.PlayerHealth;
        values[WireCodes.UnitValue.MaxHealth] = _combat.EffectiveMaxHealth(staminaBonus);
        values[WireCodes.UnitValue.Power] = _spells.Power;
        values[WireCodes.UnitValue.MaxPower] = _spells.EffectiveMaxPower(wisdomBonus);
        AddAttributesTo(values, now);
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Level] = _level,
            [WireCodes.UnitValue.Health] = _combat.PlayerHealth,
            [WireCodes.UnitValue.MaxHealth] = _combat.EffectiveMaxHealth(staminaBonus),
            [WireCodes.UnitValue.Power] = _spells.Power,
            [WireCodes.UnitValue.MaxPower] = _spells.EffectiveMaxPower(wisdomBonus),
        }, now);
    }
}
