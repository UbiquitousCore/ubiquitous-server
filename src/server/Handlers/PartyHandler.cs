using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class PartyHandler(ISessionPlayer player, SessionOutbox outbox, PartyService party)
{
    private readonly PartyService _party = party;

    public PartyRoster? MyRoster => player.Character is null ? null : _party.RosterOf(player.UnitId);

    public void Tick(DateTimeOffset now)
    {
        _party.TickKickVotes(now);
        _party.TickOffline(now);
    }

    public bool IsInParty => player.Character is not null && _party.RosterOf(player.UnitId) is not null;

    public IReadOnlyList<string> MemberIdsExcludingSelf()
    {
        if (player.Character is null) return [];
        var myId = player.UnitId;
        var roster = _party.RosterOf(myId);
        return roster is null ? [] : [.. roster.Members.Select(m => m.Id).Where(id => id != myId)];
    }

    public async Task<IReadOnlyList<string>> Invite(JsonElement payload)
    {
        if (player.Character is null || player.World is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inviting to a party.")];

        var targetSession = PayloadIds.NetworkId(payload) is { } networkId
            ? player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(networkId))
            : PayloadIds.Read(payload, "name") is { Length: > 0 } name
                ? player.Hub?.FindByCharacterName(name)
                : null;
        if (targetSession?.Presence is not { } targetPresence || !player.Sees(targetSession))
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, "Player is not online.")];

        var targetName = targetPresence.Name;
        var result = _party.Invite(player.UnitId, player.Character.Name, targetPresence.NetworkId.Text!, targetName);
        if (result.Code != PartyResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(result.Code))];

        if (player.Hub is not null)
        {
            var invited = SessionOutbox.Unsequenced(WebMessageTypes.PartyInvited,
                new PartyInvitedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name));
            await player.Hub.DeliverAsync(targetSession, invited);
        }

        return [outbox.Reply(WebMessageTypes.PartyInviteSent, new PartyInviteSentPayload(targetPresence.NetworkId, targetName))];
    }

    public async Task<IReadOnlyList<string>> Accept()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before accepting a party invite.")];

        var result = _party.Accept(player.UnitId);
        if (result.Code != PartyResultCode.Ok || result.Roster is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(result.Roster))];
    }

    public async Task<IReadOnlyList<string>> Decline()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before declining a party invite.")];

        var result = _party.Decline(player.UnitId);
        if (result.Code != PartyResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(result.Code))];

        if (player.Hub is not null && result.InviterId is not null)
        {
            var inviterSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(result.InviterId));
            if (inviterSession is not null)
            {
                var declined = SessionOutbox.Unsequenced(WebMessageTypes.PartyDeclined,
                    new PartyDeclinedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name));
                await player.Hub.DeliverAsync(inviterSession, declined);
            }
        }

        return [outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(null))];
    }

    public async Task<IReadOnlyList<string>> Leave()
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before leaving a party.")];

        var result = _party.Leave(player.UnitId);
        if (result.Code != PartyResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(null))];
    }

    public async Task<IReadOnlyList<string>> Kick(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before kicking a party member.")];

        var request = WebMessageJson.DeserializePayload<PartyKickPayload>(payload);
        var targetId = request.NetworkId.Text;
        if (targetId is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, "A member to kick is required.")];

        var result = _party.Kick(player.UnitId, targetId);
        if (result.Code != PartyResultCode.Ok)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(result.Code))];

        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);

        if (player.Hub is not null && result.RemovedMemberId is not null)
        {
            var kickedSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(result.RemovedMemberId));
            if (kickedSession is not null)
            {
                var kicked = SessionOutbox.Unsequenced(WebMessageTypes.PartyKicked,
                    new PartyRosterPayload(null, []));
                await player.Hub.DeliverAsync(kickedSession, kicked);
            }
        }

        return [outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(result.Roster))];
    }

    public IReadOnlyList<string> VoteKick(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before asking to remove a party member.")];
        var request = WebMessageJson.DeserializePayload<PartyVoteKickPayload>(payload);
        var targetId = request.NetworkId.Text;
        if (targetId is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidParty, "A member to remove is required.")];

        var code = _party.StartKickVote(player.UnitId, targetId, player.Now);
        return code == PartyResultCode.Ok ? DrainNotices() : [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(code))];
    }

    public IReadOnlyList<string> AnswerVoteKick(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before answering a vote.")];
        var request = WebMessageJson.DeserializePayload<PartyVoteKickAnswerPayload>(payload);
        var code = _party.AnswerKickVote(player.UnitId, request.Agree, player.Now);
        return code == PartyResultCode.Ok ? DrainNotices() : [outbox.Error(WireCodes.ProtocolError.InvalidParty, PartyRefusalReason(code))];
    }

    public async Task<IReadOnlyList<string>> RejoinAsync()
    {
        var result = _party.MarkOnline(player.UnitId, player.Now);
        if (result.Roster is null) return [];
        await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
        return [outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(result.Roster))];
    }

    public async Task GoOfflineAsync()
    {
        if (player.Character is null) return;
        if (player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(player.UnitId)) is not null) return;
        var result = _party.MarkOffline(player.UnitId, player.Now);
        if (result.Roster is not null) await NotifyRosterAsync(result.Roster, result.NotifyMemberIds);
    }

    public IReadOnlyList<string> DrainNotices()
    {
        if (player.Character is null) return [];
        var responses = new List<string>();
        foreach (var notice in _party.DrainNotices(player.UnitId))
        {
            switch (notice)
            {
                case KickVoteAsked asked:
                    responses.Add(outbox.Reply(WebMessageTypes.PartyVoteKickAsked, new PartyVoteKickAskedPayload(
                        WebNetworkId.FromString(asked.InitiatorId), asked.InitiatorName,
                        WebNetworkId.FromString(asked.TargetId), asked.TargetName)));
                    break;
                case KickVoteRefused refused:
                    responses.Add(outbox.Reply(WebMessageTypes.PartyVoteKickRefused, new PartyVoteKickRefusedPayload(
                        WebNetworkId.FromString(refused.VoterId), refused.VoterName,
                        WebNetworkId.FromString(refused.InitiatorId), refused.InitiatorName)));
                    break;
                case KickVoteEnded ended:
                    responses.Add(outbox.Reply(WebMessageTypes.PartyVoteKickEnded, new PartyVoteKickEndedPayload(
                        WebNetworkId.FromString(ended.TargetId), ended.TargetName, ended.Passed)));
                    break;
                case KickVoteRemoved:
                    responses.Add(outbox.Reply(WebMessageTypes.PartyKicked, new PartyRosterPayload(null, [])));
                    responses.Add(outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(null)));
                    break;
                case PartyRosterChanged:
                    responses.Add(outbox.Reply(WebMessageTypes.PartyRoster, RosterPayload(_party.RosterOf(player.UnitId))));
                    break;
            }
        }
        return responses;
    }

    private async Task NotifyRosterAsync(PartyRoster? roster, IReadOnlyList<string> memberIds)
    {
        if (player.Hub is null) return;
        var raw = SessionOutbox.Unsequenced(WebMessageTypes.PartyRoster, RosterPayload(roster));
        foreach (var memberId in memberIds)
        {
            if (string.Equals(memberId, player.UnitId, StringComparison.Ordinal)) continue;
            var session = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(memberId));
            if (session is not null) await player.Hub.DeliverAsync(session, raw);
        }
    }

    private static PartyRosterPayload RosterPayload(PartyRoster? roster) => roster is null
        ? new PartyRosterPayload(null, [])
        : new PartyRosterPayload(roster.PartyId, [.. roster.Members.Select(member =>
            new PartyRosterMemberPayload(WebNetworkId.FromString(member.Id), member.Name, member.IsLeader, member.Online))]);

    private static string PartyRefusalReason(PartyResultCode code) => code switch
    {
        PartyResultCode.SelfInvite => "You cannot invite yourself.",
        PartyResultCode.TargetAlreadyInParty => "Player is already in a party.",
        PartyResultCode.AlreadyPending => "Player already has a pending invite.",
        PartyResultCode.NotLeader => "Only the party leader can do that.",
        PartyResultCode.PartyFull => "The party is full.",
        PartyResultCode.NoPendingInvite => "There is no pending invite.",
        PartyResultCode.NotInParty => "You are not in a party.",
        PartyResultCode.CannotKickSelf => "You cannot kick yourself.",
        PartyResultCode.TargetNotInParty => "That player is not in your party.",
        PartyResultCode.LeaderKicks => "The party leader removes members without a vote.",
        PartyResultCode.KickVoteUnderWay => "A vote is already under way.",
        PartyResultCode.NoKickVote => "There is no vote to answer.",
        _ => "Party request refused.",
    };
}
