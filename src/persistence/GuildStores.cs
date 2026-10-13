using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileGuildStore(string directory) : IGuildStore
{
    public Task<IReadOnlyList<PersistedGuild>> ListAllAsync(CancellationToken cancellationToken = default) =>
        JsonFile.ReadEachAsync<PersistedGuild>(directory, "guild-*.json", cancellationToken);

    public Task SaveAsync(PersistedGuild guild, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(PathFor(guild.Id), guild, cancellationToken);

    public Task DeleteAsync(string guildId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(guildId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string guildId) => Path.Combine(directory, $"guild-{guildId}.json");
}

public sealed class InMemoryGuildStore : IGuildStore
{
    private readonly Dictionary<string, PersistedGuild> _byId = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<PersistedGuild>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersistedGuild> result = _byId.Values.ToArray();
        return Task.FromResult(result);
    }

    public Task SaveAsync(PersistedGuild guild, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byId[guild.Id] = guild;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string guildId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byId.Remove(guildId);
        return Task.CompletedTask;
    }
}
