using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class EmoteHandler(ISessionPlayer player, SessionOutbox outbox, EmoteService emotes)
{
    public IReadOnlyList<string> Perform(JsonElement payload)
    {
        if (player.World is null || player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before emoting.")];

        var request = WebMessageJson.DeserializePayload<EmoteRequestPayload>(payload);
        var senderId = player.UnitId;
        var emote = emotes.Create(new EmoteRequest(request.Name), senderId);
        return emote is null
            ? [outbox.Error(WireCodes.ProtocolError.InvalidEmote, "Unknown emote.")]
            : [outbox.Reply(WebMessageTypes.EmotePerformed,
                new EmotePerformedPayload(WebNetworkId.FromString(senderId), emote.Id, emote.Name))];
    }
}
