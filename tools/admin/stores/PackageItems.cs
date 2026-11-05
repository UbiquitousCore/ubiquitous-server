using Ubiquitous.Data;

namespace Ubiquitous.Admin.Stores;

// The items in the imported data package, read once on first use. An import
// that has not run yet leaves the list empty rather than stopping the page.
public sealed class PackageItems(string dataDirectory) : IItemCatalog
{
    private readonly Lazy<IReadOnlyDictionary<int, AdminItem>> _items = new(() => Load(dataDirectory));

    public AdminItem? Find(int id) => _items.Value.TryGetValue(id, out var item) ? item : null;

    public string NameOf(int id) => Find(id)?.Name ?? $"item {id}";

    public IReadOnlyList<AdminItem> Search(string text, int limit)
    {
        if (text.Length == 0) return [];
        if (int.TryParse(text, out var id)) return Find(id) is { } exact ? [exact] : [];
        return [.. _items.Value.Values
            .Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => !item.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)];
    }

    private static IReadOnlyDictionary<int, AdminItem> Load(string directory)
    {
        if (!File.Exists(Path.Combine(directory, DataManifest.FileName))) return new Dictionary<int, AdminItem>();
        return DataCatalog.LoadFromDirectory(directory).LoadItems()
            .ToDictionary(item => (int)item.Id, item => new AdminItem((int)item.Id, item.NameKey, (int)item.MaxStack));
    }
}
