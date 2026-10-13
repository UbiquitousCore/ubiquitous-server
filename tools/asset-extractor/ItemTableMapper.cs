using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record ItemTableColumns(
    int Id, int Name, int Class, int SubClass, int DisplayInfoId, int NameTextId = -1,
    int OnUseSpellId = -1, int OnUseSpellTrigger = -1, int OnUseCooldownSeconds = -1, int BuyPrice = -1,
    int SellPrice = -1, int QuestId = -1, int AllowableClassMask = -1,
    int InventoryType = -1, int RequiredLevel = -1, int AllowableRaceMask = -1,
    int Quality = -1,
    int MinDamage = -1, int MaxDamage = -1, int SpeedMs = -1, int Armor = -1,
    int StatCount = -1, int FirstStat = -1, int MaxStats = 10, int RequiredSpellId = -1);

public static class ItemTableMapper
{
    public static IReadOnlyList<ItemDefinition> MapItems(
        FTableDocument document, ItemTableColumns columns, Func<uint, string>? resolveName = null)
    {
        if (columns.Name < 0 && resolveName is null)
        {
            throw new FTableFormatException(
                "This table has no usable name column; a resolveName delegate is required.");
        }

        var items = new List<ItemDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate item id {id}.");
            }

            string name = columns.Name >= 0
                ? RequireString(row, columns.Name, i, "name")
                : resolveName!(RequireUInt(row, columns.NameTextId, i, "name string id"));
            byte @class = RequireByte(row, columns.Class, i, "class");
            byte subClass = RequireByte(row, columns.SubClass, i, "subclass");
            uint displayInfoId = RequireUInt(row, columns.DisplayInfoId, i, "display");

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FTableFormatException($"Item {id} has a blank name.");
            }

            uint? onUseSpellId = null;
            if (columns.OnUseSpellId >= 0 && columns.OnUseSpellTrigger >= 0
                && row[columns.OnUseSpellId] is uint spellId && spellId != 0
                && row[columns.OnUseSpellTrigger] is uint trigger && trigger == 1)
            {
                onUseSpellId = spellId;
            }

            uint? onUseCooldown = onUseSpellId is not null && columns.OnUseCooldownSeconds >= 0
                && row[columns.OnUseCooldownSeconds] is uint cooldown && cooldown > 0
                ? cooldown
                : null;

            uint? buyPrice = columns.BuyPrice >= 0 && row[columns.BuyPrice] is uint price ? price : null;
            uint? sellPrice = columns.SellPrice >= 0 && row[columns.SellPrice] is uint sell ? sell : null;

            uint? questId = columns.QuestId >= 0 && columns.QuestId < row.Count
                && row[columns.QuestId] is uint quest && quest != 0
                ? quest
                : null;

            uint? allowedClasses = columns.AllowableClassMask >= 0 && columns.AllowableClassMask < row.Count
                && row[columns.AllowableClassMask] is uint mask
                ? mask
                : null;

            uint? allowedRaces = columns.AllowableRaceMask >= 0 && columns.AllowableRaceMask < row.Count
                && row[columns.AllowableRaceMask] is uint raceBits
                ? raceBits
                : null;

            uint inventoryType = columns.InventoryType >= 0 && columns.InventoryType < row.Count
                && row[columns.InventoryType] is uint worn
                ? worn
                : 0;
            uint requiredLevel = columns.RequiredLevel >= 0 && columns.RequiredLevel < row.Count
                && row[columns.RequiredLevel] is uint level
                ? level
                : 0;

            uint? quality = columns.Quality >= 0 && columns.Quality < row.Count
                && row[columns.Quality] is uint rank
                ? rank
                : null;

            uint? minDamage = Optional(row, columns.MinDamage);
            uint? maxDamage = Optional(row, columns.MaxDamage);
            uint? speedMs = Optional(row, columns.SpeedMs);
            uint? armor = Optional(row, columns.Armor);
            uint? requiredSpellId = Optional(row, columns.RequiredSpellId);

            var stats = new List<Ubiquitous.Data.ItemAttributeBonus>();
            if (columns.StatCount >= 0 && columns.StatCount < row.Count
                && columns.FirstStat >= 0
                && row[columns.StatCount] is uint declared)
            {
                int carried = (int)Math.Min(declared, (uint)columns.MaxStats);
                for (int stat = 0; stat < carried; stat++)
                {
                    int kindColumn = columns.FirstStat + (stat * 2);
                    if (kindColumn + 1 >= row.Count) break;
                    if (Optional(row, kindColumn) is not uint kind) continue;
                    if (Optional(row, kindColumn + 1) is not uint amount) continue;
                    if (Ubiquitous.Data.ItemStatKinds.AttributeFor((int)kind) is not int attribute) continue;
                    stats.Add(new Ubiquitous.Data.ItemAttributeBonus(attribute, (int)amount));
                }
            }

            items.Add(new ItemDefinition(
                id, name, @class, subClass, displayInfoId, onUseSpellId, onUseCooldown, buyPrice, questId, sellPrice,
                allowedClasses, inventoryType, requiredLevel, allowedRaces, quality,
                minDamage, maxDamage, speedMs, armor, stats.Count == 0 ? null : stats, requiredSpellId));
        }

        return items;
    }

    private static uint? Optional(IReadOnlyList<object> row, int column)
    {
        if (column < 0 || column >= row.Count) return null;
        return row[column] switch
        {
            uint whole when whole > 0 => whole,
            float fraction when fraction > 0f => (uint)MathF.Round(fraction, MidpointRounding.AwayFromZero),
            _ => null,
        };
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable item {field}.");
        }

        return value;
    }

    private static byte RequireByte(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count) throwInvalid();
        return row[column] switch
        {
            sbyte b when b >= 0 => (byte)b,
            uint u when u <= byte.MaxValue => (byte)u,
            _ => throwInvalid(),
        };

        byte throwInvalid() => throw new FTableFormatException($"Row {index} has no usable item {field}.");
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable item {field}.");
        }

        return value;
    }
}
