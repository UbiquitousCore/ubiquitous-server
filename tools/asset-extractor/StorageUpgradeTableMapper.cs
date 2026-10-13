using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record StorageUpgradeTableColumns(
    int Id, int AreaColumn, int SlotCount, int PriceCurrency, int PriceAmount, int FromSlotCount);

public static class StorageUpgradeTableMapper
{
    public static IReadOnlyList<StorageUpgradeDefinition> MapUpgrades(
        FTableDocument document, StorageUpgradeTableColumns columns)
    {
        var upgrades = new List<StorageUpgradeDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate storage-upgrade id {id}.");
            }

            uint areaCode = RequireUInt(row, columns.AreaColumn, i, $"column {columns.AreaColumn} (bag or bank)");
            string area = areaCode switch
            {
                0 => "bag",
                1 => "bank",
                _ => throw new FTableFormatException($"Row {i} names neither the bag nor the bank in column {columns.AreaColumn}: {areaCode}."),
            };

            uint slotCount = RequireUInt(row, columns.SlotCount, i, $"column {columns.SlotCount} (slots after the upgrade)");
            uint fromSlotCount = RequireUInt(row, columns.FromSlotCount, i, $"column {columns.FromSlotCount} (slots before the upgrade)");
            uint currencyKind = RequireUInt(row, columns.PriceCurrency, i, $"column {columns.PriceCurrency} (currency)");
            uint currencyValue = RequireUInt(row, columns.PriceAmount, i, $"column {columns.PriceAmount} (price)");

            upgrades.Add(new StorageUpgradeDefinition(id, area, slotCount, fromSlotCount, currencyKind, currencyValue));
        }

        return upgrades;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable storage-upgrade {field}.");
        }

        return value;
    }
}
