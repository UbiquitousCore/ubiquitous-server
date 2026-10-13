using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record TradeRequestedPayload(
    [property: JsonPropertyName("fromId")] WebNetworkId FromId);

public sealed record TradeDeclinedPayload(
    [property: JsonPropertyName("byId")] WebNetworkId ById);

public sealed record TradeStartedPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId);

public sealed record TradeOfferPayload(
    [property: JsonPropertyName("itemIds")] IReadOnlyList<string> ItemIds,
    [property: JsonPropertyName("gold")] int Gold);

public sealed record TradeConfirmPayload(
    [property: JsonPropertyName("confirmed")] bool Confirmed);

public sealed record TradeUpdatedPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId,
    [property: JsonPropertyName("opponentGold")] int OpponentGold,
    [property: JsonPropertyName("opponentItems")] IReadOnlyList<InventoryItemPayload> OpponentItems,
    [property: JsonPropertyName("opponentConfirmed")] bool OpponentConfirmed,
    [property: JsonPropertyName("yourConfirmed")] bool YourConfirmed);

public sealed record TradeCancelledPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record TradeCompletedPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId);
