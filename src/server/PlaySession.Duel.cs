using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private const int DuelLossHealth = 10;

    private readonly DuelService _duels;

    private IReadOnlyList<string> DuelChallenge(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Character selection is required before duelling.")];
        var fromId = PlayerUnitId;
        var targetId = PayloadIds.Read(payload, "targetId");
        if (targetId is null)
            return [Error(WireCodes.ProtocolError.InvalidTarget, "A duel target is required.")];
        if (HiddenFromMe(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Player is not visible.")];

        var outcome = _duels.Request(fromId, _world.PlayerPosition, targetId, Now);
        return outcome switch
        {
            DuelService.RequestOutcome.SelfDuel => [Error(WireCodes.ProtocolError.InvalidTarget, "You cannot duel yourself.")],
            DuelService.RequestOutcome.AlreadyDuelling => [Error(WireCodes.ProtocolError.DuelRefused, "Someone here is already duelling.")],
            _ => [],
        };
    }

    private IReadOnlyList<string> DuelAccept()
    {
        if (_world is null || _character is null) return [];
        var accepterId = PlayerUnitId;
        var now = Now;
        var result = _duels.Accept(accepterId, _world.PlayerPosition, now);
        switch (result.Outcome)
        {
            case DuelService.AcceptOutcome.NoPendingRequest:
                return [Error(WireCodes.ProtocolError.DuelRefused, "There is no duel request to accept.")];
            case DuelService.AcceptOutcome.Expired:
                return [Error(WireCodes.ProtocolError.DuelRefused, "That duel request is no longer open.")];
            case DuelService.AcceptOutcome.AlreadyDuelling:
                return [Error(WireCodes.ProtocolError.DuelRefused, "Someone here is already duelling.")];
            case DuelService.AcceptOutcome.OutOfRange:
                return [Error(WireCodes.ProtocolError.DuelRefused, "You are too far away to accept that duel.")];
        }

        return [Reply(WebMessageTypes.DuelStarted,
            new DuelStartedPayload(WebNetworkId.FromString(result.OpponentId!), SecondsUntilStart(accepterId, now)))];
    }

    private int SecondsUntilStart(string playerId, DateTimeOffset now) =>
        _duels.StartsAt(playerId) is { } at ? Math.Max(0, (int)Math.Ceiling((at - now).TotalSeconds)) : 0;

    public void LeaveDuelIfAny()
    {
        if (_character is null) return;
        _duels.Leave(PlayerUnitId);
        _blows.Forget(PlayerUnitId);
    }

    private IReadOnlyList<string> DuelDecline()
    {
        if (_character is null) return [];
        _duels.Decline(PlayerUnitId);
        return [];
    }

    private IReadOnlyList<string> DuelForfeit()
    {
        if (_character is null) return [];
        var playerId = PlayerUnitId;
        return EndOwnDuel(playerId, DuelEndReason.Forfeited);
    }

    private IReadOnlyList<string> EndOwnDuel(string playerId, DuelEndReason reason)
    {
        var opponentId = _duels.End(playerId, reason);
        if (opponentId is null) return [];

        var responses = new List<string>();
        var stopped = _combat.Disengage(playerId);
        if (stopped is not null)
        {
            responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(stopped))));
            responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(playerId))));
        }
        EndHarmFrom(opponentId, Now, responses);

        var (health, maxHealth) = RestoreAfterDuel(reason);
        responses.Add(Reply(WebMessageTypes.DuelEnded,
            new DuelEndedPayload(WebNetworkId.FromString(opponentId), WireDuelReason(reason), health, maxHealth)));
        if (reason == DuelEndReason.Lost && _emotes.Create(new EmoteRequest(DuelLossEmote), playerId) is { } beg)
            responses.Add(Reply(WebMessageTypes.EmotePerformed,
                new EmotePerformedPayload(WebNetworkId.FromString(playerId), beg.Id, beg.Name)));
        return responses;
    }

    private const string DuelLossEmote = "beg";

    private IReadOnlyList<string> TickDuelNotifications(DateTimeOffset now)
    {
        if (_character is null) return [];
        var playerId = PlayerUnitId;
        var notifications = _duels.Drain(playerId);
        if (notifications.Count == 0) return [];

        var responses = new List<string>();
        foreach (var notification in notifications)
        {
            switch (notification)
            {
                case DuelChallenged requested:
                    responses.Add(Reply(WebMessageTypes.DuelChallenged,
                        new DuelChallengedPayload(WebNetworkId.FromString(requested.OpponentId))));
                    break;
                case DuelDeclined declined:
                    responses.Add(Reply(WebMessageTypes.DuelDeclined,
                        new DuelDeclinedPayload(WebNetworkId.FromString(declined.OpponentId))));
                    break;
                case DuelStarted started:
                    responses.Add(Reply(WebMessageTypes.DuelStarted,
                        new DuelStartedPayload(WebNetworkId.FromString(started.OpponentId), SecondsUntilStart(playerId, Now))));
                    break;
                case DuelOver over:
                    var stopped = _combat.Disengage(playerId);
                    if (stopped is not null)
                    {
                        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(stopped))));
                        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(playerId))));
                    }
                    EndHarmFrom(over.OpponentId, now, responses);

                    var (health, maxHealth) = RestoreAfterDuel(over.Reason);
                    responses.Add(Reply(WebMessageTypes.DuelEnded,
                        new DuelEndedPayload(WebNetworkId.FromString(over.OpponentId), WireDuelReason(over.Reason), health, maxHealth)));
                    break;
            }
        }

        return responses;
    }

    private IReadOnlyList<string> EndDuelAsLoser(string playerId) =>
        EndOwnDuel(playerId, DuelEndReason.Lost);

    private (int Health, int MaxHealth) RestoreAfterDuel(DuelEndReason reason)
    {
        var playerId = _character is null ? null : PlayerUnitId;
        var staminaBonus = playerId is null
            ? 0
            : _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, Now);
        var maxHealth = _combat.EffectiveMaxHealth(staminaBonus);
        if (reason == DuelEndReason.Lost)
        {
            var restored = _combat.IsPlayerDead
                ? _combat.ReviveTo(DuelLossHealth, staminaBonus)
                : _combat.HealPlayer(Math.Max(0, DuelLossHealth - _combat.PlayerHealth), staminaBonus);
            if (playerId is not null) _combat.ClearDeath(playerId);
            var health = restored?.FinalHealth ?? _combat.PlayerHealth;
            if (playerId is not null)
                MarkUnitValues(playerId, new Dictionary<string, int>
                {
                    [WireCodes.UnitValue.Health] = health,
                    [WireCodes.UnitValue.MaxHealth] = maxHealth,
                }, Now);
            return (health, maxHealth);
        }

        return (_combat.PlayerHealth, maxHealth);
    }

    private static string WireDuelReason(DuelEndReason reason) => reason switch
    {
        DuelEndReason.Won => "won",
        DuelEndReason.Lost => "lost",
        DuelEndReason.Forfeited => "forfeited",
        DuelEndReason.OpponentForfeited => "opponentForfeited",
        _ => "ended",
    };
}
