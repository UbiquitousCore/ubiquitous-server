using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record DuelChallengedPayload(
    [property: JsonPropertyName("fromId")] WebNetworkId FromId);

public sealed record DuelDeclinedPayload(
    [property: JsonPropertyName("byId")] WebNetworkId ById);

public sealed record DuelStartedPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId,
    [property: JsonPropertyName("startsIn")] int StartsIn = 0);

public sealed record DuelEndedPayload(
    [property: JsonPropertyName("opponentId")] WebNetworkId OpponentId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("health")] int Health,
    [property: JsonPropertyName("maxHealth")] int MaxHealth);
