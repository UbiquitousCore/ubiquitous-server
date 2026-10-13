using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileMailStore(string directory) : IMailStore
{
    public async Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<List<PersistedMailMessage>>(PathFor(recipientGuid), cancellationToken) ?? [];

    public Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(PathFor(recipientGuid), messages, cancellationToken);

    private string PathFor(string recipientGuid) => Path.Combine(directory, $"mail-{recipientGuid}.json");
}

public sealed class InMemoryMailStore : IMailStore
{
    private readonly Dictionary<string, List<PersistedMailMessage>> _byRecipient = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersistedMailMessage> result = _byRecipient.TryGetValue(recipientGuid, out var list) ? list.ToArray() : [];
        return Task.FromResult(result);
    }

    public Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byRecipient[recipientGuid] = messages.ToList();
        return Task.CompletedTask;
    }
}
