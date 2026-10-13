namespace Ubiquitous.Game;

public enum PartyResultCode
{
    Ok,
    SelfInvite,
    TargetAlreadyInParty,
    AlreadyPending,
    NotLeader,
    PartyFull,
    NoPendingInvite,
    NotInParty,
    CannotKickSelf,
    TargetNotInParty,
    LeaderKicks,
    KickVoteUnderWay,
    NoKickVote,
}

public abstract record PartyNotice;

public sealed record KickVoteAsked(string InitiatorId, string InitiatorName, string TargetId, string TargetName) : PartyNotice;

public sealed record KickVoteRefused(string VoterId, string VoterName, string InitiatorId, string InitiatorName) : PartyNotice;

public sealed record KickVoteEnded(string TargetId, string TargetName, bool Passed) : PartyNotice;

public sealed record KickVoteRemoved : PartyNotice;

public sealed record PartyRosterChanged : PartyNotice;

public sealed record PartyMember(string Id, string Name, bool IsLeader, bool Online);

public sealed record PartyRoster(string PartyId, IReadOnlyList<PartyMember> Members);

public sealed record PartyInviteResult(PartyResultCode Code);

public sealed record PartyDeclineResult(PartyResultCode Code, string? InviterId, string? InviterName);

public sealed record PartyChangeResult(
    PartyResultCode Code,
    PartyRoster? Roster,
    IReadOnlyList<string> NotifyMemberIds,
    string? RemovedMemberId = null);

public sealed class PartyService
{
    public const int MaxPartySize = 4;

    private sealed class Party
    {
        public required string Id { get; init; }
        public required string LeaderId { get; set; }
        public List<string> MemberIds { get; } = [];
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    private sealed record PendingInvite(string? ExistingPartyId, string InviterId, string InviterName, string TargetName);

    private readonly Lock Gate = new();
    private readonly Dictionary<string, Party> Parties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> MemberParty = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingInvite> Invites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KickVote> Votes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PartyNotice>> Mailbox = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> OfflineSince = new(StringComparer.Ordinal);
    private int _nextPartyId = 1;

    public static readonly TimeSpan KickVoteTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan OfflineGrace = TimeSpan.FromMinutes(5);

    private sealed class KickVote
    {
        public required string InitiatorId { get; init; }
        public required string InitiatorName { get; init; }
        public required string TargetId { get; init; }
        public required string TargetName { get; init; }
        public required DateTimeOffset EndsAt { get; init; }
        public HashSet<string> Asked { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Agreed { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Refused { get; } = new(StringComparer.Ordinal);
    }

    public static int KickVotesNeeded(int membersBesideTarget) => membersBesideTarget / 2 + 1;

    public PartyInviteResult Invite(string inviterId, string inviterName, string targetId, string targetName)
    {
        lock (Gate)
        {
            if (string.Equals(inviterId, targetId, StringComparison.Ordinal))
                return new PartyInviteResult(PartyResultCode.SelfInvite);
            if (MemberParty.ContainsKey(targetId))
                return new PartyInviteResult(PartyResultCode.TargetAlreadyInParty);
            if (Invites.ContainsKey(targetId))
                return new PartyInviteResult(PartyResultCode.AlreadyPending);

            string? existingPartyId = null;
            if (MemberParty.TryGetValue(inviterId, out var partyId))
            {
                var party = Parties[partyId];
                if (!string.Equals(party.LeaderId, inviterId, StringComparison.Ordinal))
                    return new PartyInviteResult(PartyResultCode.NotLeader);
                if (party.MemberIds.Count >= MaxPartySize)
                    return new PartyInviteResult(PartyResultCode.PartyFull);
                existingPartyId = partyId;
            }

            Invites[targetId] = new PendingInvite(existingPartyId, inviterId, inviterName, targetName);
            return new PartyInviteResult(PartyResultCode.Ok);
        }
    }

    public PartyChangeResult Accept(string targetId)
    {
        lock (Gate)
        {
            if (!Invites.Remove(targetId, out var invite))
                return new PartyChangeResult(PartyResultCode.NoPendingInvite, null, []);

            if (invite.ExistingPartyId is { } existingId && Parties.TryGetValue(existingId, out var party))
            {
                if (party.MemberIds.Count >= MaxPartySize)
                    return new PartyChangeResult(PartyResultCode.PartyFull, null, []);
                party.MemberIds.Add(targetId);
                party.Names[targetId] = invite.TargetName;
                MemberParty[targetId] = existingId;
                return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(party),
                    [.. party.MemberIds.Where(id => id != targetId)]);
            }

            var newId = (_nextPartyId++).ToString();
            var created = new Party { Id = newId, LeaderId = invite.InviterId };
            created.MemberIds.Add(invite.InviterId);
            created.MemberIds.Add(targetId);
            created.Names[invite.InviterId] = invite.InviterName;
            created.Names[targetId] = invite.TargetName;
            Parties[newId] = created;
            MemberParty[invite.InviterId] = newId;
            MemberParty[targetId] = newId;
            return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(created), [invite.InviterId]);
        }
    }

    public PartyDeclineResult Decline(string targetId)
    {
        lock (Gate)
        {
            return Invites.Remove(targetId, out var invite)
                ? new PartyDeclineResult(PartyResultCode.Ok, invite.InviterId, invite.InviterName)
                : new PartyDeclineResult(PartyResultCode.NoPendingInvite, null, null);
        }
    }

    public PartyChangeResult Leave(string memberId)
    {
        lock (Gate)
        {
            if (!MemberParty.TryGetValue(memberId, out var partyId))
                return new PartyChangeResult(PartyResultCode.NotInParty, null, []);

            var party = Parties[partyId];
            CallOffVoteLocked(party);
            RemoveMemberLocked(party, memberId);

            if (party.MemberIds.Count < 2)
            {
                var survivors = DisbandLocked(party);
                return new PartyChangeResult(PartyResultCode.Ok, null, survivors);
            }

            if (string.Equals(party.LeaderId, memberId, StringComparison.Ordinal))
                party.LeaderId = party.MemberIds[0];

            return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(party), [.. party.MemberIds]);
        }
    }

    public PartyChangeResult Kick(string leaderId, string targetId)
    {
        lock (Gate)
        {
            if (!MemberParty.TryGetValue(leaderId, out var partyId))
                return new PartyChangeResult(PartyResultCode.NotInParty, null, []);
            var party = Parties[partyId];
            if (!string.Equals(party.LeaderId, leaderId, StringComparison.Ordinal))
                return new PartyChangeResult(PartyResultCode.NotLeader, null, []);
            if (string.Equals(leaderId, targetId, StringComparison.Ordinal))
                return new PartyChangeResult(PartyResultCode.CannotKickSelf, null, []);
            if (!party.MemberIds.Contains(targetId))
                return new PartyChangeResult(PartyResultCode.TargetNotInParty, null, []);

            CallOffVoteLocked(party);
            RemoveMemberLocked(party, targetId);

            if (party.MemberIds.Count < 2)
            {
                var survivors = DisbandLocked(party);
                return new PartyChangeResult(PartyResultCode.Ok, null, survivors, targetId);
            }

            return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(party),
                [.. party.MemberIds.Where(id => id != leaderId)], targetId);
        }
    }

    public PartyResultCode StartKickVote(string initiatorId, string targetId, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!MemberParty.TryGetValue(initiatorId, out var partyId))
                return PartyResultCode.NotInParty;
            var party = Parties[partyId];
            if (string.Equals(party.LeaderId, initiatorId, StringComparison.Ordinal))
                return PartyResultCode.LeaderKicks;
            if (string.Equals(initiatorId, targetId, StringComparison.Ordinal))
                return PartyResultCode.CannotKickSelf;
            if (!party.MemberIds.Contains(targetId))
                return PartyResultCode.TargetNotInParty;
            if (Votes.ContainsKey(partyId))
                return PartyResultCode.KickVoteUnderWay;

            var vote = new KickVote
            {
                InitiatorId = initiatorId,
                InitiatorName = party.Names[initiatorId],
                TargetId = targetId,
                TargetName = party.Names[targetId],
                EndsAt = now + KickVoteTimeout,
            };
            foreach (var member in party.MemberIds)
                if (member != initiatorId && member != targetId) vote.Asked.Add(member);
            Votes[partyId] = vote;
            foreach (var voter in vote.Asked)
                Tell(voter, new KickVoteAsked(initiatorId, vote.InitiatorName, targetId, vote.TargetName));
            SettleLocked(party, vote);
            return PartyResultCode.Ok;
        }
    }

    public PartyResultCode AnswerKickVote(string voterId, bool agree, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!MemberParty.TryGetValue(voterId, out var partyId)
                || !Votes.TryGetValue(partyId, out var vote)
                || !vote.Asked.Contains(voterId)
                || vote.Agreed.Contains(voterId) || vote.Refused.Contains(voterId))
                return PartyResultCode.NoKickVote;
            var party = Parties[partyId];
            if (agree) vote.Agreed.Add(voterId);
            else RefuseLocked(party, vote, voterId);
            SettleLocked(party, vote);
            return PartyResultCode.Ok;
        }
    }

    public void TickKickVotes(DateTimeOffset now)
    {
        lock (Gate)
        {
            foreach (var (partyId, vote) in Votes.ToArray())
            {
                if (now < vote.EndsAt) continue;
                var party = Parties[partyId];
                foreach (var silent in vote.Asked.Where(id => !vote.Agreed.Contains(id) && !vote.Refused.Contains(id)).ToArray())
                    RefuseLocked(party, vote, silent);
                SettleLocked(party, vote);
            }
        }
    }

    public IReadOnlyList<PartyNotice> DrainNotices(string memberId)
    {
        lock (Gate)
        {
            return Mailbox.Remove(memberId, out var notices) ? notices : [];
        }
    }

    private void Tell(string memberId, PartyNotice notice)
    {
        if (!Mailbox.TryGetValue(memberId, out var notices)) Mailbox[memberId] = notices = [];
        notices.Add(notice);
    }

    private void RefuseLocked(Party party, KickVote vote, string voterId)
    {
        vote.Refused.Add(voterId);
        var refused = new KickVoteRefused(voterId, party.Names.GetValueOrDefault(voterId, string.Empty),
            vote.InitiatorId, vote.InitiatorName);
        Tell(vote.InitiatorId, refused);
        foreach (var other in vote.Asked)
            if (other != voterId) Tell(other, refused);
    }

    private void SettleLocked(Party party, KickVote vote)
    {
        var needed = KickVotesNeeded(vote.Asked.Count + 1);
        var agreed = vote.Agreed.Count + 1;
        var unanswered = vote.Asked.Count - vote.Agreed.Count - vote.Refused.Count;
        bool passed = agreed >= needed;
        if (!passed && agreed + unanswered >= needed) return;

        Votes.Remove(party.Id);
        var ended = new KickVoteEnded(vote.TargetId, vote.TargetName, passed);
        Tell(vote.InitiatorId, ended);
        foreach (var voter in vote.Asked) Tell(voter, ended);
        if (!passed) return;

        RemoveMemberLocked(party, vote.TargetId);
        Tell(vote.TargetId, new KickVoteRemoved());
        if (party.MemberIds.Count < 2)
        {
            foreach (var survivor in DisbandLocked(party)) Tell(survivor, new PartyRosterChanged());
            return;
        }
        if (string.Equals(party.LeaderId, vote.TargetId, StringComparison.Ordinal))
            party.LeaderId = party.MemberIds[0];
        foreach (var member in party.MemberIds) Tell(member, new PartyRosterChanged());
    }

    private void CallOffVoteLocked(Party party)
    {
        if (!Votes.Remove(party.Id, out var vote)) return;
        var ended = new KickVoteEnded(vote.TargetId, vote.TargetName, Passed: false);
        Tell(vote.InitiatorId, ended);
        foreach (var voter in vote.Asked) Tell(voter, ended);
    }

    public PartyChangeResult MarkOffline(string memberId, DateTimeOffset now)
    {
        lock (Gate)
        {
            Invites.Remove(memberId);
            if (!MemberParty.TryGetValue(memberId, out var partyId))
                return new PartyChangeResult(PartyResultCode.NotInParty, null, []);
            OfflineSince[memberId] = now;
            var party = Parties[partyId];
            return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(party),
                [.. party.MemberIds.Where(id => id != memberId)]);
        }
    }

    public PartyChangeResult MarkOnline(string memberId, DateTimeOffset now)
    {
        lock (Gate)
        {
            DropOfflineLocked(now);
            Mailbox.Remove(memberId);
            if (!MemberParty.TryGetValue(memberId, out var partyId))
                return new PartyChangeResult(PartyResultCode.NotInParty, null, []);
            OfflineSince.Remove(memberId);
            var party = Parties[partyId];
            return new PartyChangeResult(PartyResultCode.Ok, RosterOfLocked(party),
                [.. party.MemberIds.Where(id => id != memberId)]);
        }
    }

    public void TickOffline(DateTimeOffset now)
    {
        lock (Gate) DropOfflineLocked(now);
    }

    private void DropOfflineLocked(DateTimeOffset now)
    {
        foreach (var (memberId, since) in OfflineSince.ToArray())
        {
            if (now - since < OfflineGrace) continue;
            OfflineSince.Remove(memberId);
            if (!MemberParty.TryGetValue(memberId, out var partyId)) continue;
            var party = Parties[partyId];
            CallOffVoteLocked(party);
            RemoveMemberLocked(party, memberId);
            if (party.MemberIds.Count < 2)
            {
                foreach (var survivor in DisbandLocked(party)) Tell(survivor, new PartyRosterChanged());
                continue;
            }
            if (string.Equals(party.LeaderId, memberId, StringComparison.Ordinal))
                party.LeaderId = party.MemberIds[0];
            foreach (var member in party.MemberIds) Tell(member, new PartyRosterChanged());
        }
    }

    public PartyRoster? RosterOf(string memberId)
    {
        lock (Gate)
        {
            return MemberParty.TryGetValue(memberId, out var partyId) ? RosterOfLocked(Parties[partyId]) : null;
        }
    }

    private void RemoveMemberLocked(Party party, string memberId)
    {
        party.MemberIds.Remove(memberId);
        party.Names.Remove(memberId);
        MemberParty.Remove(memberId);
        OfflineSince.Remove(memberId);
    }

    private IReadOnlyList<string> DisbandLocked(Party party)
    {
        var survivors = party.MemberIds.ToArray();
        foreach (var survivor in survivors)
        {
            MemberParty.Remove(survivor);
            OfflineSince.Remove(survivor);
        }
        Parties.Remove(party.Id);
        return survivors;
    }

    private PartyRoster RosterOfLocked(Party party) => new(party.Id,
        [.. party.MemberIds.Select(id => new PartyMember(id, party.Names[id], id == party.LeaderId,
            !OfflineSince.ContainsKey(id)))]);
}
