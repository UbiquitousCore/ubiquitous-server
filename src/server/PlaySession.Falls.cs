using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly FallTracker _fall = new();

    // Follows the reported movement state; a ground landing after a jump is
    // checked against the height actually fallen.
    private void FollowFall(MovementState state, float y, DateTimeOffset now, List<string> responses)
    {
        switch (state)
        {
            case MovementState.Jumping:
                _fall.Jumping(y);
                break;
            case MovementState.Falling:
                _fall.Falling(y);
                break;
            case MovementState.Landed:
            case MovementState.LandedMoving:
                if (_fall.Landed(y) is { } height) HurtByFall(height, now, responses);
                break;
            default:
                _fall.Cancel();
                break;
        }
    }

    private void HurtByFall(float height, DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _combat.IsPlayerDead) return;
        var playerId = PlayerUnitId;
        var stamina = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
        var damage = FallDamage.DamageFor(FallDamage.LandingSpeed(height), _combat.EffectiveMaxHealth(stamina));
        if (_combat.HurtPlayerByWorld(playerId, damage, now, stamina) is not { } hurt) return;

        var self = WebNetworkId.FromString(playerId);
        responses.Add(Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
            self, self, 0, WireCodes.Outcome.Fall, hurt.Damage, hurt.FinalHealth)));
        MarkUnitValues(playerId, new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Health] = hurt.FinalHealth,
            [WireCodes.UnitValue.MaxHealth] = hurt.MaxHealth,
        }, now);
        if (hurt.Killed) DieByFall(responses);
    }

    // No one killed the player, so nothing else stands down; the gold
    // penalty is the one for dying to anything but another player.
    private void DieByFall(List<string> responses)
    {
        var playerId = PlayerUnitId;
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(WebNetworkId.FromString(playerId))));
        EndEffectsOnSelf(null, Now, responses);
        responses.AddRange(ApplyDeathPenalty(playerId, killedByPlayer: false));
        UnmarkEngaged();
        StandDownPack(responses);
        MarkFightsReturning();
        _combat.Disengage(playerId);
        if (_duels.OpponentOf(playerId) is not null)
            responses.AddRange(EndDuelAsLoser(playerId));
    }
}
