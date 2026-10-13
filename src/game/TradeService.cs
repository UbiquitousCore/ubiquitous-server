using System.Collections.Concurrent;
using Ubiquitous.Common;

namespace Ubiquitous.Game;

public sealed class TradeService
{
    public const double TradeRange = InteractionRange.Max;

    private sealed record PendingRequest(string FromId, Position FromPosition);

    public sealed record TradeOffer(IReadOnlyList<string> ItemIds, int Gold, bool Confirmed)
    {
        public static readonly TradeOffer Empty = new([], 0, false);
    }

    private readonly ConcurrentDictionary<string, PendingRequest> _pendingByTarget = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _activePartner = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TradeOffer> _offers = new(StringComparer.Ordinal);

    public enum RequestOutcome { Ok, SelfTrade, AlreadyTrading }

    public RequestOutcome Request(string fromId, Position fromPosition, string toId)
    {
        if (string.Equals(fromId, toId, StringComparison.Ordinal)) return RequestOutcome.SelfTrade;
        if (_activePartner.ContainsKey(fromId) || _activePartner.ContainsKey(toId)) return RequestOutcome.AlreadyTrading;

        _pendingByTarget[toId] = new PendingRequest(fromId, fromPosition);
        return RequestOutcome.Ok;
    }

    public enum AcceptOutcome { Ok, NoPendingRequest, AlreadyTrading, OutOfRange }

    public sealed record AcceptResult(AcceptOutcome Outcome, string? PartnerId);

    public AcceptResult Accept(string accepterId, Position accepterPosition)
    {
        if (!_pendingByTarget.TryRemove(accepterId, out var pending))
            return new AcceptResult(AcceptOutcome.NoPendingRequest, null);

        if (_activePartner.ContainsKey(accepterId) || _activePartner.ContainsKey(pending.FromId))
            return new AcceptResult(AcceptOutcome.AlreadyTrading, null);

        if (Distance(pending.FromPosition, accepterPosition) > TradeRange)
            return new AcceptResult(AcceptOutcome.OutOfRange, pending.FromId);

        _activePartner[accepterId] = pending.FromId;
        _activePartner[pending.FromId] = accepterId;
        _offers[accepterId] = TradeOffer.Empty;
        _offers[pending.FromId] = TradeOffer.Empty;
        return new AcceptResult(AcceptOutcome.Ok, pending.FromId);
    }

    public string? Decline(string declinerId) =>
        _pendingByTarget.TryRemove(declinerId, out var pending) ? pending.FromId : null;

    public string? PartnerOf(string playerId) => _activePartner.TryGetValue(playerId, out var partner) ? partner : null;

    public TradeOffer? OfferOf(string playerId) => _offers.TryGetValue(playerId, out var offer) ? offer : null;

    public enum OfferOutcome { Ok, NotTrading, CancelledByChange }

    public sealed record OfferResult(OfferOutcome Outcome, string? PartnerId);

    public OfferResult SetOffer(string playerId, IReadOnlyList<string> itemIds, int gold)
    {
        if (!_activePartner.TryGetValue(playerId, out var partnerId))
            return new OfferResult(OfferOutcome.NotTrading, null);

        var mine = _offers[playerId];
        var theirs = _offers[partnerId];
        if (mine.Confirmed || theirs.Confirmed)
        {
            CancelInternal(playerId);
            return new OfferResult(OfferOutcome.CancelledByChange, partnerId);
        }

        _offers[playerId] = new TradeOffer(itemIds, Math.Max(0, gold), false);
        return new OfferResult(OfferOutcome.Ok, partnerId);
    }

    public enum ConfirmOutcome { Ok, NotTrading, BothConfirmed }

    public sealed record ConfirmResult(ConfirmOutcome Outcome, string? PartnerId);

    public ConfirmResult Confirm(string playerId, bool confirmed)
    {
        if (!_activePartner.TryGetValue(playerId, out var partnerId))
            return new ConfirmResult(ConfirmOutcome.NotTrading, null);

        _offers[playerId] = _offers[playerId] with { Confirmed = confirmed };
        return confirmed && _offers[partnerId].Confirmed
            ? new ConfirmResult(ConfirmOutcome.BothConfirmed, partnerId)
            : new ConfirmResult(ConfirmOutcome.Ok, partnerId);
    }

    public string? Cancel(string playerId) => CancelInternal(playerId);

    private string? CancelInternal(string playerId)
    {
        if (!_activePartner.TryRemove(playerId, out var partnerId)) return null;
        _activePartner.TryRemove(partnerId, out _);
        _offers.TryRemove(playerId, out _);
        _offers.TryRemove(partnerId, out _);
        return partnerId;
    }

    private static double Distance(Position a, Position b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
