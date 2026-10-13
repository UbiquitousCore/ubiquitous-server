namespace Ubiquitous.Common;

public sealed record PersistedGuildMember(string MemberId, string Name, bool IsLeader);

public sealed record PersistedGuild(string Id, string Name, IReadOnlyList<PersistedGuildMember> Members);

public interface IGuildStore
{
    Task<IReadOnlyList<PersistedGuild>> ListAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PersistedGuild guild, CancellationToken cancellationToken = default);
    Task DeleteAsync(string guildId, CancellationToken cancellationToken = default);
}
