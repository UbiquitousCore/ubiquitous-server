using System.Text.Json;
using Ubiquitous.Common;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class ReportHandler(ISessionPlayer player, SessionOutbox outbox, ICharacterStore characters, IReportStore store)
{
    private readonly ReportService _reports = new(store);

    public async Task<string> Submit(JsonElement payload)
    {
        if (player.Character is null)
            return outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before reporting a player.");

        var request = WebMessageJson.DeserializePayload<ReportPlayerPayload>(payload);
        var targetGuid = PlayerWireIds.GuidOf(request.TargetId);
        if (targetGuid is null) return outbox.Error(WireCodes.ProtocolError.InvalidTarget, "A target is required.");

        if (string.Equals(targetGuid, player.Character.Guid, StringComparison.Ordinal))
            return outbox.Error(WireCodes.ProtocolError.InvalidTarget, "You cannot report yourself.");

        var target = await characters.GetByGuidAsync(targetGuid);
        if (target is null) return outbox.Error(WireCodes.ProtocolError.InvalidTarget, "That player does not exist.");

        var outcome = await _reports.SubmitAsync(
            player.Character.Guid, player.Character.Name, targetGuid, target.Name, request.Reason);
        return outcome switch
        {
            ReportOutcome.SelfTarget => outbox.Error(WireCodes.ProtocolError.InvalidTarget, "You cannot report yourself."),
            ReportOutcome.EmptyReason => outbox.Error(WireCodes.ProtocolError.InvalidReport, "A reason is required."),
            _ => outbox.Reply(WebMessageTypes.SocialReportAcknowledged, new { }),
        };
    }
}
