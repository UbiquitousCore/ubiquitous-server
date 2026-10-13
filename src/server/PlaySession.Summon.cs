using System.Text.Json;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    internal void ReceiveSummon(SummonOffer offer) => _summonHandler.Receive(offer);

    private async Task<IReadOnlyList<string>> SummonAnswer(JsonElement payload)
    {
        var (accepted, replies) = await _summonHandler.Answer(payload);
        if (accepted is null) return replies;
        var responses = new List<string>(replies);
        TravelTo(responses, accepted.MapId, accepted.Position, accepted.Yaw);
        return responses;
    }
}
