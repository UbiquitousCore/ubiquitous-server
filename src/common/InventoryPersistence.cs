namespace Ubiquitous.Common;

public sealed record PersistedInventoryItem(string Id, int Entry, int Count, string Area, int Slot);

public interface IInventoryStore
{
    Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(string characterGuid, CancellationToken cancellationToken = default);
    Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items, CancellationToken cancellationToken = default);
}
