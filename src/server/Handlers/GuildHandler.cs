using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class GuildHandler(ISessionPlayer player, SessionOutbox outbox, GuildService guilds)
{
    public async Task<IReadOnlyList<string>> Found(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before founding a guild.")];

        var request = WebMessageJson.DeserializePayload<GuildFoundPayload>(payload);
        var result = await guilds.Found(player.UnitId, player.Character.Name, player.Character.Level, request.Name ?? string.Empty);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(result.Roster))];
    }

    public async Task<IReadOnlyList<string>> Invite(JsonElement payload)
    {
        if (player.Character is null || player.World is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inviting to a guild.")];

        var request = WebMessageJson.DeserializePayload<GuildInvitePayload>(payload);
        var targetSession = player.Hub?.FindByCharacterNetworkId(request.NetworkId);
        if (targetSession is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, "Player is not online.")];

        var targetPresence = targetSession.Presence!;
        var targetName = targetPresence.Name;
        var result = await guilds.Invite(player.UnitId, request.NetworkId.Text!, targetName);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        if (player.Hub is not null)
        {
            var invited = SessionOutbox.Unsequenced(WebMessageTypes.GuildInvited,
                new GuildInvitedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name, result.GuildName!));
            await player.Hub.DeliverAsync(targetSession, invited);
        }

        return [outbox.Reply(WebMessageTypes.GuildInviteSent, new GuildInviteSentPayload(request.NetworkId, targetName))];
    }

    public async Task<IReadOnlyList<string>> Accept()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before accepting a guild invite.")];

        var result = await guilds.Accept(player.UnitId);
        if (result.Code != GuildResultCode.Ok || result.Roster is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(result.Roster))];
    }

    public async Task<IReadOnlyList<string>> Decline()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before declining a guild invite.")];

        var result = await guilds.Decline(player.UnitId);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        if (player.Hub is not null && result.InviterId is not null)
        {
            var inviterSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(result.InviterId));
            if (inviterSession is not null)
            {
                var declined = SessionOutbox.Unsequenced(WebMessageTypes.GuildDeclined,
                    new GuildDeclinedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name));
                await player.Hub.DeliverAsync(inviterSession, declined);
            }
        }

        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(null))];
    }

    public async Task<IReadOnlyList<string>> Leave()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before leaving a guild.")];

        var result = await guilds.Leave(player.UnitId);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(null))];
    }

    public async Task<IReadOnlyList<string>> Kick(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before kicking a guild member.")];

        var request = WebMessageJson.DeserializePayload<GuildKickPayload>(payload);
        var targetId = request.NetworkId.Text;
        if (targetId is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, "A member to kick is required.")];

        var result = await guilds.Kick(player.UnitId, targetId);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);

        if (player.Hub is not null && result.RemovedMemberId is not null)
        {
            var kickedSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(result.RemovedMemberId));
            if (kickedSession is not null)
            {
                var kicked = SessionOutbox.Unsequenced(WebMessageTypes.GuildKicked,
                    new GuildRosterPayload(null, null, []));
                await player.Hub.DeliverAsync(kickedSession, kicked);
            }
        }

        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(result.Roster))];
    }

    public async Task<IReadOnlyList<string>> Disband()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before disbanding a guild.")];

        var result = await guilds.Disband(player.UnitId);
        if (result.Code != GuildResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidGuild, GuildRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(null))];
    }

    public async Task<IReadOnlyList<string>> Roster()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before checking a guild roster.")];

        var roster = await guilds.RosterOf(player.UnitId);
        return [outbox.Reply(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(roster))];
    }

    private async Task NotifyRosterAsync(GuildRoster? roster, IReadOnlyList<string> memberIds)
    {
        if (player.Hub is null) return;
        var raw = SessionOutbox.Unsequenced(WebMessageTypes.GuildRoster, GuildRosterPayloadFor(roster));
        foreach (var memberId in memberIds)
        {
            if (string.Equals(memberId, player.UnitId, StringComparison.Ordinal)) continue;
            var session = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(memberId));
            if (session is not null) await player.Hub.DeliverAsync(session, raw);
        }
    }

    private static GuildRosterPayload GuildRosterPayloadFor(GuildRoster? roster) => roster is null
        ? new GuildRosterPayload(null, null, [])
        : new GuildRosterPayload(roster.GuildId, roster.GuildName, [.. roster.Members.Select(member =>
            new GuildRosterMemberPayload(WebNetworkId.FromString(member.Id), member.Name, member.IsLeader))]);

    private static string GuildRefusalReason(GuildResultCode code) => code switch
    {
        GuildResultCode.AlreadyInGuild => "You are already in a guild.",
        GuildResultCode.InvalidName => $"A guild name cannot be blank or longer than {GuildService.MaxNameLength} characters.",
        GuildResultCode.NameTaken => "That guild name is already taken.",
        GuildResultCode.TooLowLevel => $"You must be at least level {GuildService.MinFoundingLevel} to found a guild.",
        GuildResultCode.SelfInvite => "You cannot invite yourself.",
        GuildResultCode.TargetAlreadyInGuild => "Player is already in a guild.",
        GuildResultCode.AlreadyPending => "Player already has a pending invite.",
        GuildResultCode.NotLeader => "Only the guild leader can do that.",
        GuildResultCode.NoPendingInvite => "There is no pending invite.",
        GuildResultCode.NotInGuild => "You are not in a guild.",
        GuildResultCode.CannotKickSelf => "You cannot kick yourself.",
        GuildResultCode.TargetNotInGuild => "That player is not in your guild.",
        GuildResultCode.GuildFull => "The guild is full.",
        _ => "Guild request refused.",
    };
}
