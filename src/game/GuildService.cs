using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum GuildResultCode
{
    Ok,
    AlreadyInGuild,
    InvalidName,
    NameTaken,
    TooLowLevel,
    SelfInvite,
    TargetAlreadyInGuild,
    AlreadyPending,
    NotLeader,
    NoPendingInvite,
    NotInGuild,
    CannotKickSelf,
    TargetNotInGuild,
    GuildFull,
}

public sealed record GuildMember(string Id, string Name, bool IsLeader);

public sealed record GuildRoster(string GuildId, string GuildName, IReadOnlyList<GuildMember> Members);

public sealed record GuildFoundResult(GuildResultCode Code, GuildRoster? Roster = null);

public sealed record GuildInviteResult(GuildResultCode Code, string? GuildName = null);

public sealed record GuildDeclineResult(GuildResultCode Code, string? InviterId, string? InviterName, string? GuildName);

public sealed record GuildChangeResult(
    GuildResultCode Code,
    GuildRoster? Roster,
    IReadOnlyList<string> NotifyMemberIds,
    string? RemovedMemberId = null);

public sealed class GuildService
{
    public const int MaxNameLength = 16;

    public const int MinFoundingLevel = 10;

    public const int MaxMembers = 240;

    private readonly ServiceGate _gate = new();
    private readonly IGuildStore _store;

    public GuildService(IGuildStore store) => _store = store;

    private sealed class Guild
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public required string LeaderId { get; set; }
        public List<string> MemberIds { get; } = [];
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    private sealed record PendingInvite(string GuildId, string GuildName, string InviterId, string InviterName, string TargetName);

    private readonly Lock InviteGate = new();
    private readonly Dictionary<string, PendingInvite> Invites = new(StringComparer.Ordinal);

    public Task<GuildFoundResult> Found(string founderId, string founderName, int founderLevel, string guildName, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => FoundCore(founderId, founderName, founderLevel, guildName, cancellationToken), cancellationToken);

    private async Task<GuildFoundResult> FoundCore(string founderId, string founderName, int founderLevel, string guildName, CancellationToken cancellationToken)
    {
        var trimmedName = guildName?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > MaxNameLength)
            return new GuildFoundResult(GuildResultCode.InvalidName);
        if (founderLevel < MinFoundingLevel)
            return new GuildFoundResult(GuildResultCode.TooLowLevel);

        var all = await _store.ListAllAsync(cancellationToken);
        if (all.Any(g => g.Members.Any(m => string.Equals(m.MemberId, founderId, StringComparison.Ordinal))))
            return new GuildFoundResult(GuildResultCode.AlreadyInGuild);
        if (all.Any(g => string.Equals(g.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            return new GuildFoundResult(GuildResultCode.NameTaken);

        var guild = new Guild { Id = Guid.NewGuid().ToString("n"), Name = trimmedName, LeaderId = founderId };
        guild.MemberIds.Add(founderId);
        guild.Names[founderId] = founderName;

        await _store.SaveAsync(Snapshot(guild), cancellationToken);
        return new GuildFoundResult(GuildResultCode.Ok, RosterOfLocal(guild));
    }

    public Task<GuildInviteResult> Invite(string inviterId, string targetId, string targetName, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => InviteCore(inviterId, targetId, targetName, cancellationToken), cancellationToken);

    private async Task<GuildInviteResult> InviteCore(string inviterId, string targetId, string targetName, CancellationToken cancellationToken)
    {
        if (string.Equals(inviterId, targetId, StringComparison.Ordinal))
            return new GuildInviteResult(GuildResultCode.SelfInvite);

        var all = await _store.ListAllAsync(cancellationToken);
        var guild = FindByMember(all, inviterId);
        if (guild is null) return new GuildInviteResult(GuildResultCode.NotInGuild);
        if (!string.Equals(guild.Members.First(m => m.IsLeader).MemberId, inviterId, StringComparison.Ordinal))
            return new GuildInviteResult(GuildResultCode.NotLeader);
        if (FindByMember(all, targetId) is not null)
            return new GuildInviteResult(GuildResultCode.TargetAlreadyInGuild);
        if (guild.Members.Count >= MaxMembers)
            return new GuildInviteResult(GuildResultCode.GuildFull);

        lock (InviteGate)
        {
            if (Invites.ContainsKey(targetId))
                return new GuildInviteResult(GuildResultCode.AlreadyPending);

            var inviterName = guild.Members.First(m => string.Equals(m.MemberId, inviterId, StringComparison.Ordinal)).Name;
            Invites[targetId] = new PendingInvite(guild.Id, guild.Name, inviterId, inviterName, targetName);
        }

        return new GuildInviteResult(GuildResultCode.Ok, guild.Name);
    }

    public Task<GuildChangeResult> Accept(string targetId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => AcceptCore(targetId, cancellationToken), cancellationToken);

    private async Task<GuildChangeResult> AcceptCore(string targetId, CancellationToken cancellationToken)
    {
        PendingInvite invite;
        lock (InviteGate)
        {
            if (!Invites.Remove(targetId, out var found))
                return new GuildChangeResult(GuildResultCode.NoPendingInvite, null, []);
            invite = found;
        }

        var all = await _store.ListAllAsync(cancellationToken);
        var persisted = all.FirstOrDefault(g => string.Equals(g.Id, invite.GuildId, StringComparison.Ordinal));
        if (persisted is null) return new GuildChangeResult(GuildResultCode.NotInGuild, null, []);
        if (persisted.Members.Count >= MaxMembers)
            return new GuildChangeResult(GuildResultCode.GuildFull, null, []);

        var guild = Load(persisted);
        guild.MemberIds.Add(targetId);
        guild.Names[targetId] = invite.TargetName;

        await _store.SaveAsync(Snapshot(guild), cancellationToken);
        return new GuildChangeResult(GuildResultCode.Ok, RosterOfLocal(guild),
            [.. guild.MemberIds.Where(id => id != targetId)]);
    }

    public Task<GuildDeclineResult> Decline(string targetId)
    {
        lock (InviteGate)
        {
            return Task.FromResult(Invites.Remove(targetId, out var invite)
                ? new GuildDeclineResult(GuildResultCode.Ok, invite.InviterId, invite.InviterName, invite.GuildName)
                : new GuildDeclineResult(GuildResultCode.NoPendingInvite, null, null, null));
        }
    }

    public Task<GuildChangeResult> Leave(string memberId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => LeaveCore(memberId, cancellationToken), cancellationToken);

    private async Task<GuildChangeResult> LeaveCore(string memberId, CancellationToken cancellationToken)
    {
        var all = await _store.ListAllAsync(cancellationToken);
        var persisted = FindByMember(all, memberId);
        if (persisted is null) return new GuildChangeResult(GuildResultCode.NotInGuild, null, []);

        if (string.Equals(persisted.Members.First(m => m.IsLeader).MemberId, memberId, StringComparison.Ordinal))
            return await DissolveAsync(persisted, cancellationToken);

        var guild = Load(persisted);
        RemoveMember(guild, memberId);
        await _store.SaveAsync(Snapshot(guild), cancellationToken);
        return new GuildChangeResult(GuildResultCode.Ok, RosterOfLocal(guild), [.. guild.MemberIds]);
    }

    public Task<GuildChangeResult> Kick(string leaderId, string targetId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => KickCore(leaderId, targetId, cancellationToken), cancellationToken);

    private async Task<GuildChangeResult> KickCore(string leaderId, string targetId, CancellationToken cancellationToken)
    {
        if (string.Equals(leaderId, targetId, StringComparison.Ordinal))
            return new GuildChangeResult(GuildResultCode.CannotKickSelf, null, []);

        var all = await _store.ListAllAsync(cancellationToken);
        var persisted = FindByMember(all, leaderId);
        if (persisted is null) return new GuildChangeResult(GuildResultCode.NotInGuild, null, []);
        if (!string.Equals(persisted.Members.First(m => m.IsLeader).MemberId, leaderId, StringComparison.Ordinal))
            return new GuildChangeResult(GuildResultCode.NotLeader, null, []);
        if (!persisted.Members.Any(m => string.Equals(m.MemberId, targetId, StringComparison.Ordinal)))
            return new GuildChangeResult(GuildResultCode.TargetNotInGuild, null, []);

        var guild = Load(persisted);
        RemoveMember(guild, targetId);

        await _store.SaveAsync(Snapshot(guild), cancellationToken);
        return new GuildChangeResult(GuildResultCode.Ok, RosterOfLocal(guild),
            [.. guild.MemberIds.Where(id => id != leaderId)], targetId);
    }

    public Task<GuildChangeResult> Disband(string leaderId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => DisbandCore(leaderId, cancellationToken), cancellationToken);

    private async Task<GuildChangeResult> DisbandCore(string leaderId, CancellationToken cancellationToken)
    {
        var all = await _store.ListAllAsync(cancellationToken);
        var persisted = FindByMember(all, leaderId);
        if (persisted is null) return new GuildChangeResult(GuildResultCode.NotInGuild, null, []);
        if (!string.Equals(persisted.Members.First(m => m.IsLeader).MemberId, leaderId, StringComparison.Ordinal))
            return new GuildChangeResult(GuildResultCode.NotLeader, null, []);

        return await DissolveAsync(persisted, cancellationToken);
    }

    public Task<GuildRoster?> RosterOf(string memberId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => RosterOfCore(memberId, cancellationToken), cancellationToken);

    private async Task<GuildRoster?> RosterOfCore(string memberId, CancellationToken cancellationToken)
    {
        var all = await _store.ListAllAsync(cancellationToken);
        var guild = FindByMember(all, memberId);
        return guild is null ? null : new GuildRoster(guild.Id, guild.Name,
            [.. guild.Members.Select(m => new GuildMember(m.MemberId, m.Name, m.IsLeader))]);
    }

    private async Task<GuildChangeResult> DissolveAsync(PersistedGuild persisted, CancellationToken cancellationToken)
    {
        await _store.DeleteAsync(persisted.Id, cancellationToken);
        var leaderId = persisted.Members.First(m => m.IsLeader).MemberId;
        return new GuildChangeResult(GuildResultCode.Ok, null,
            [.. persisted.Members.Select(m => m.MemberId).Where(id => id != leaderId)]);
    }

    private static PersistedGuild? FindByMember(IReadOnlyList<PersistedGuild> all, string memberId) =>
        all.FirstOrDefault(g => g.Members.Any(m => string.Equals(m.MemberId, memberId, StringComparison.Ordinal)));

    private static Guild Load(PersistedGuild persisted)
    {
        var leader = persisted.Members.FirstOrDefault(m => m.IsLeader) ?? persisted.Members[0];
        var guild = new Guild { Id = persisted.Id, Name = persisted.Name, LeaderId = leader.MemberId };
        foreach (var member in persisted.Members)
        {
            guild.MemberIds.Add(member.MemberId);
            guild.Names[member.MemberId] = member.Name;
        }

        return guild;
    }

    private static void RemoveMember(Guild guild, string memberId)
    {
        guild.MemberIds.Remove(memberId);
        guild.Names.Remove(memberId);
    }

    private static PersistedGuild Snapshot(Guild guild) => new(guild.Id, guild.Name,
        [.. guild.MemberIds.Select(id => new PersistedGuildMember(id, guild.Names[id], id == guild.LeaderId))]);

    private static GuildRoster RosterOfLocal(Guild guild) => new(guild.Id, guild.Name,
        [.. guild.MemberIds.Select(id => new GuildMember(id, guild.Names[id], id == guild.LeaderId))]);
}
