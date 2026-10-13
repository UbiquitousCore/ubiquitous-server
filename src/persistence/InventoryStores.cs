using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileInventoryStore(string directory) : IInventoryStore
{
    public async Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(string characterGuid, CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<List<PersistedInventoryItem>>(PathFor(characterGuid), cancellationToken) ?? [];

    public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(PathFor(characterGuid), items, cancellationToken);

    private string PathFor(string characterGuid) => Path.Combine(directory, $"inventory-{characterGuid}.json");
}
