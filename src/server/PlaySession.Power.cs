using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private void NotePlayerStruck(int damage, StrikeResult result, DateTimeOffset now)
    {
        var power = result is StrikeResult.Dodge or StrikeResult.Parry or StrikeResult.Block
            ? _spells.GainMoraleFromAvoid()
            : damage > 0 ? _spells.GainMoraleFromHitTaken(damage) ?? _spells.LoseVigorFromHitTaken(damage) : null;
        if (power is not null) MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = power.Value }, now);
    }

    private void NotePlayerLanded(CombatSwing swing, int meleeAttackSpeedPercent, DateTimeOffset now)
    {
        if (swing.Ranged || swing.Damage <= 0 || swing.Result is not (StrikeResult.Hit or StrikeResult.Critical)) return;
        var power = _spells.GainMoraleFromHitDealt(
            swing.Damage, CombatService.EffectiveSwingInterval(meleeAttackSpeedPercent),
            swing.Result == StrikeResult.Critical, swing.OffHand);
        if (power is not null) MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = power.Value }, now);
    }
}
