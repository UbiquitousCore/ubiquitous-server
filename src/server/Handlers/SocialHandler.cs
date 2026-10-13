using System.Text.Json;
using Ubiquitous.Common;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class SocialHandler(ISessionPlayer player, SessionOutbox outbox, ICharacterStore characters, IFriendStore store)
{
    private readonly FriendService _friends = new(store);

    public Task<string> AddFriend(JsonElement payload) =>
        Add(payload, SocialListKind.Friend, "You cannot add yourself as a friend.");

    public Task<string> AddIgnore(JsonElement payload) =>
        Add(payload, SocialListKind.Ignore, "You cannot ignore yourself.");

    public Task<string> RemoveFriend(JsonElement payload) => Remove(payload, SocialListKind.Friend);

    public Task<string> RemoveIgnore(JsonElement payload) => Remove(payload, SocialListKind.Ignore);

    public async Task<string> List()
    {
        if (player.Character is null)
            return outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before checking your social lists.");
        return outbox.Reply(WebMessageTypes.SocialListUpdated, ToPayload(await _friends.ListAsync(player.Character.Guid)));
    }

    private async Task<string> Add(JsonElement payload, SocialListKind kind, string selfTargetReason)
    {
        if (player.Character is null)
            return outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before adding a friend.");

        var request = WebMessageJson.DeserializePayload<SocialTargetPayload>(payload);
        var target = PlayerWireIds.GuidOf(request.TargetId) is { } guid
            ? await characters.GetByGuidAsync(guid)
            : !string.IsNullOrWhiteSpace(request.Name)
                ? await characters.FindByNameAsync(request.Name.Trim())
                : null;
        if (target is null)
            return outbox.Error(WireCodes.ProtocolError.InvalidTarget,
                request.TargetId is null && request.Name is null ? "A target is required." : "That player does not exist.");
        var targetGuid = target.Guid;

        if (string.Equals(targetGuid, player.Character.Guid, StringComparison.Ordinal))
            return outbox.Error(WireCodes.ProtocolError.InvalidTarget, selfTargetReason);

        var result = await _friends.AddAsync(player.Character.Guid, kind, targetGuid, target.Name);
        return result.Outcome == SocialAddOutcome.SelfTarget
            ? outbox.Error(WireCodes.ProtocolError.InvalidTarget, selfTargetReason)
            : outbox.Reply(WebMessageTypes.SocialListUpdated, ToPayload(result.List!));
    }

    public string Who()
    {
        if (player.Character is null)
            return outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before listing players.");
        var online = player.Hub?.OnlinePlayers() ?? [];
        return outbox.Reply(WebMessageTypes.SocialWhoList, new SocialWhoListPayload([.. online.Select(presence =>
            new SocialEntryPayload(presence.NetworkId, presence.Name, true, presence.Level, presence.Race,
                presence.Class, presence.Gender, presence.MapId, presence.Position))]));
    }

    private async Task<string> Remove(JsonElement payload, SocialListKind kind)
    {
        if (player.Character is null)
            return outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before removing a friend.");

        var targetGuid = TargetGuid(payload);
        if (targetGuid is null) return outbox.Error(WireCodes.ProtocolError.InvalidTarget, "A target is required.");

        var list = await _friends.RemoveAsync(player.Character.Guid, kind, targetGuid);
        return outbox.Reply(WebMessageTypes.SocialListUpdated, ToPayload(list));
    }

    private static string? TargetGuid(JsonElement payload) =>
        PlayerWireIds.GuidOf(WebMessageJson.DeserializePayload<SocialTargetPayload>(payload).TargetId);

    private SocialListUpdatedPayload ToPayload(SocialListState list) =>
        new([.. list.Friends.Select(ToEntryPayload)], [.. list.Ignored.Select(ToEntryPayload)]);

    private SocialEntryPayload ToEntryPayload(SocialEntry entry)
    {
        var networkId = WebNetworkId.FromString(PlayerWireIds.For(entry.CharacterGuid));
        var presence = player.Hub?.FindByCharacterNetworkId(networkId)?.Presence;
        return presence is null
            ? new SocialEntryPayload(networkId, entry.Name)
            : new SocialEntryPayload(networkId, presence.Name, true, presence.Level, presence.Race,
                presence.Class, presence.Gender, presence.MapId, presence.Position);
    }
}
