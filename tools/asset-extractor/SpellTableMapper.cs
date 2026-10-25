using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record SpellTableColumns(
    int Id, int Name, int CastTimeMs, int CooldownMs, int MaxRange, int NameTextId = -1,
    int DurationMs = -1, int WornSlotNeeded = -1, int RequiredItemClass = -1,
    int ItemSubclassesAllowed = -1, int Speed = -1, int Rank = -1,
    int PowerCost = -1, IReadOnlyList<int>? GroupMaskColumns = null, IReadOnlyList<int>? EffectSlots = null);

public static class SpellTableMapper
{
    public static IReadOnlyList<SpellDefinition> MapSpells(
        FTableDocument document, SpellTableColumns columns, Func<uint, string>? resolveName = null)
    {
        if (columns.Name < 0 && resolveName is null)
        {
            throw new FTableFormatException(
                "This table has no usable name column; a resolveName delegate is required.");
        }

        var spells = new List<SpellDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate spell id {id}.");
            }

            string name = columns.Name >= 0
                ? RequireString(row, columns.Name, i, "name")
                : resolveName!(RequireUInt(row, columns.NameTextId, i, "name string id"));
            int castTimeMs = RequireInt(row, columns.CastTimeMs, i, "cast time");
            int cooldownMs = RequireInt(row, columns.CooldownMs, i, "cooldown");
            float maxRange = RequireFloat(row, columns.MaxRange, i, "range");

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FTableFormatException($"Spell {id} has a blank name.");
            }

            if (castTimeMs < 0 || cooldownMs < 0)
            {
                throw new FTableFormatException($"Spell {id} has a negative cast or cooldown time.");
            }

            if (!float.IsFinite(maxRange) || maxRange < 0)
            {
                throw new FTableFormatException($"Spell {id} has an unusable range.");
            }

            int durationMs = columns.DurationMs >= 0
                ? RequireInt(row, columns.DurationMs, i, "duration")
                : 0;
            if (durationMs < 0)
            {
                throw new FTableFormatException($"Spell {id} has a negative duration.");
            }

            int wornSlot = columns.WornSlotNeeded >= 0
                ? RequireInt(row, columns.WornSlotNeeded, i, $"column {columns.WornSlotNeeded} (worn slot needed)")
                : -1;
            if (wornSlot < 0 || wornSlot >= EquipmentSlots.Count)
            {
                wornSlot = -1;
            }

            uint itemClass = columns.RequiredItemClass >= 0
                ? RequireUInt(row, columns.RequiredItemClass, i, $"column {columns.RequiredItemClass} (item class needed)")
                : 0;
            uint allowedSubclasses = columns.ItemSubclassesAllowed >= 0
                ? RequireUInt(row, columns.ItemSubclassesAllowed, i, $"column {columns.ItemSubclassesAllowed} (item subclasses allowed)")
                : 0;

            float speed = columns.Speed >= 0 ? RequireFloat(row, columns.Speed, i, "speed") : 0f;
            if (!float.IsFinite(speed) || speed < 0)
            {
                throw new FTableFormatException($"Spell {id} has an unusable speed.");
            }

            int rank = columns.Rank >= 0 && columns.Rank < row.Count && row[columns.Rank] is uint ranked ? (int)ranked : 0;

            spells.Add(new SpellDefinition(
                id, name, castTimeMs, cooldownMs, maxRange, DurationMs: durationMs, Speed: speed, Rank: rank,
                WornSlotNeeded: wornSlot,
                RequiredItemClass: wornSlot < 0 ? 0 : itemClass,
                ItemSubclassesAllowed: wornSlot < 0 ? 0 : allowedSubclasses));
        }

        return spells;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable spell {field}.");
        }

        return value;
    }

    private static int RequireInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable spell {field}.");
        }

        return row[column] is uint u ? checked((int)u) : (int)row[column];
    }

    private static float RequireFloat(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not float value)
        {
            throw new FTableFormatException($"Row {index} has no usable spell {field}.");
        }

        return value;
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable spell {field}.");
        }

        return value;
    }
}
