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
