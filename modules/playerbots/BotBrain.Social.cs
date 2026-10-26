using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Modules.Playerbots;

// Duels and trades a bot is offered.
public sealed partial class BotBrain
{
    private static readonly TimeSpan MostTradeTime = TimeSpan.FromSeconds(60);
    private DateTimeOffset _tradeOpenedAt;

    private BotDecision? AnswerDuel(BotView view)
    {
        if (view.DuelAskedBy is null) return null;
        view.DuelAskedBy = null;
        var accept = settings.AcceptDuels && !view.InCombat && view.TradingWith is null && view.HealthFraction > 0.8f;
        return new SendMessage(accept ? WebMessageTypes.DuelAccept : WebMessageTypes.DuelDecline, new { });
    }

    private BotDecision? DuelReady(BotView view, DateTimeOffset now)
    {
        if (view.DuelOpponent is not { } rival || now >= view.DuelStartsAt)
        {
            _duelReadyFor = null;
            return null;
        }
        if (!string.Equals(_duelReadyFor, rival, StringComparison.Ordinal))
        {
            _duelReadyFor = rival;
            _duelReadyFaced = false;
        }
        if (!view.Units.TryGetValue(rival, out var opponent) || opponent.Dead) return Hold.Instance;
        const float gap = 4f;
        if (Flat(view.Position, opponent.Position) < gap - 0.5f)
        {
            _duelReadyFaced = false;
            var dx = view.Position.X - opponent.Position.X;
            var dz = view.Position.Z - opponent.Position.Z;
            var len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.01) { dx = 0; dz = 1; len = 1; }
            return new WalkTo(new Ubiquitous.Common.Position(
                opponent.Position.X + (float)(dx / len * gap),
                view.Position.Y,
                opponent.Position.Z + (float)(dz / len * gap)), 0.5f, "duel");
        }
        if (_duelReadyFaced) return Hold.Instance;
        _duelReadyFaced = true;
        return new SendMessage(WebMessageTypes.PlayerMove, new PlayerMovePayload(
            view.Position.X, view.Position.Y, view.Position.Z,
            Math.Atan2(opponent.Position.X - view.Position.X, -(opponent.Position.Z - view.Position.Z)),
            false, MovementState.Standing));
    }

    // A group mate's summons is answered yes, as a player would come to their group; anyone
    // else's, or one that arrives mid-fight or mid-trade, is turned down.
    private static BotDecision? AnswerSummon(BotView view)
    {
        if (view.SummonAskedBy is not { } from) return null;
        view.SummonAskedBy = null;
        var accept = view.PartyMemberIds.Contains(from) && !view.InCombat && view.TradingWith is null;
        return new SendMessage(WebMessageTypes.SummonAnswer, new SummonAnswerPayload(accept));
    }

    // Only group mates may trade with a bot: a player hands their bot gear, and the bot wears it.
    private static BotDecision? AnswerTrade(BotView view)
    {
        if (view.TradeAskedBy is not { } from) return null;
        view.TradeAskedBy = null;
        if (!view.PartyMemberIds.Contains(from) || view.TradingWith is not null || view.InCombat)
            return new SendMessage(WebMessageTypes.TradeDecline, new { });
        return new SendMessage(WebMessageTypes.TradeAccept, new { });
    }

    private BotDecision? Trade(BotView view, DateTimeOffset now)
    {
        if (view.TradingWith is null)
        {
            _tradeOpenedAt = default;
            return null;
        }

        if (_tradeOpenedAt == default) _tradeOpenedAt = now;
        if (now - _tradeOpenedAt > MostTradeTime)
        {
            _tradeOpenedAt = default;
            return new SendMessage(WebMessageTypes.TradeCancel, new { });
        }

        if (view.TradeTheyConfirmed && !view.TradeIConfirmed)
            return new SendMessage(WebMessageTypes.TradeConfirm, new TradeConfirmPayload(true));
        return Hold.Instance;
    }
}
