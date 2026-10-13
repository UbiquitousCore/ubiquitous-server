using System.Text.Json;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class PatrolHandler(ISessionPlayer player, SessionOutbox outbox, PatrolRouteLibrary library)
{
    public IReadOnlyList<string> Request()
    {
        if (Refusal() is { } refused) return refused;

        var mapId = player.World!.MapId;
        var file = library.FileFor(mapId);
        return [outbox.Reply(WebMessageTypes.PatrolFile, new PatrolFilePayload(mapId, file?.FileName, file?.Text))];
    }

    public IReadOnlyList<string> Save(JsonElement payload)
    {
        if (Refusal() is { } refused) return refused;

        var request = WebMessageJson.DeserializePayload<PatrolSavePayload>(payload);
        var saved = library.Save(request.FileName, request.Text ?? string.Empty);
        if (saved.Refusal is { } reason) return [outbox.Error(WireCodes.ProtocolError.PatrolRefused, reason)];

        return [outbox.Reply(WebMessageTypes.PatrolSaved, new PatrolSavedPayload(request.FileName, saved.RouteCount))];
    }

    private IReadOnlyList<string>? Refusal()
    {
        if (player.World is null || player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before editing patrol routes.")];
        if (!player.IsAdmin)
            return [outbox.Error(WireCodes.ProtocolError.Forbidden, "Patrol routes are an operator facility.")];
        return null;
    }
}
