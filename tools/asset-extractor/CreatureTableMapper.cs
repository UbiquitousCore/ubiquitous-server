using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record CreatureTableColumns(
    int Entry,
    int Name,
    int Level,
    int Health,
    int MinDamage = -1,
    int MaxDamage = -1,
    int RespawnMinSeconds = -1,
    int RespawnMaxSeconds = -1,
    int MoneyDropMin = -1,
    int MoneyDropMax = -1,
    int MeleeAttackIntervalMs = -1,
    int NoticeDistance = -1,
    int LeashRange = -1,
    int NpcFlags = -1,
    int ExtraFlags = -1,
    int Faction = -1,
    int Festival = -1,
    int Rank = -1,
    int RangedAttackIntervalMs = -1,
    int Family = -1);

public static class CreatureTableMapper
{
    public static IReadOnlyList<CreatureDefinition> MapCreatures(
        FTableDocument document, CreatureTableColumns columns, Func<uint, string>? resolveName = null)
    {
        if (columns.Name < 0 && resolveName is null)
        {
            throw new FTableFormatException(
                "This table has no name column; a resolveName delegate is required.");
        }

        var creatures = new List<CreatureDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint entry = RequireUInt(row, columns.Entry, i, "entry");
            string name = columns.Name >= 0 ? RequireString(row, columns.Name, i, "name") : resolveName!(entry);
            int level = RequireInt(row, columns.Level, i, "level");
            int health = RequireInt(row, columns.Health, i, "health");

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FTableFormatException($"Row {i} has a blank creature name.");
            }

            if (level < 0 || health < 0)
            {
                throw new FTableFormatException(
                    $"Creature {entry} has a negative level or health.");
            }

            if (seen.Add(entry))
            {
                var (minDamage, maxDamage) = NormalizeRange(
                    OptionalInt(row, columns.MinDamage), OptionalInt(row, columns.MaxDamage));
                var (respawnMin, respawnMax) = NormalizeRange(
                    OptionalInt(row, columns.RespawnMinSeconds), OptionalInt(row, columns.RespawnMaxSeconds));
                var (lootMin, lootMax) = NormalizeRange(
                    OptionalInt(row, columns.MoneyDropMin), OptionalInt(row, columns.MoneyDropMax));
                creatures.Add(new CreatureDefinition(
                    entry, name, level, health,
                    minDamage, maxDamage, respawnMin, respawnMax, lootMin, lootMax,
                    OptionalInt(row, columns.MeleeAttackIntervalMs),
                    OptionalInt(row, columns.NoticeDistance),
                    OptionalFloat(row, columns.LeashRange),
                    OptionalUInt(row, columns.NpcFlags) ?? 0,
                    OptionalUInt(row, columns.ExtraFlags) ?? 0,
                    OptionalInt(row, columns.Faction),
                    RankOf(OptionalUInt(row, columns.Rank)),
                    FestivalId: OptionalUInt(row, columns.Festival) ?? 0,
                    RangedAttackIntervalMs: OptionalInt(row, columns.RangedAttackIntervalMs),
                    Family: OptionalInt(row, columns.Family)));
            }
        }

        return creatures;
    }

    private static CreatureRank RankOf(uint? column) =>
        column is { } rank && Enum.IsDefined(typeof(CreatureRank), (int)rank)
            ? (CreatureRank)(int)rank
            : CreatureRank.Ordinary;

    private static (int? Min, int? Max) NormalizeRange(int? min, int? max) =>
        min is not null && max is not null && min > max ? (null, null) : (min, max);

    private static int? OptionalInt(IReadOnlyList<object> row, int column)
    {
        if (column < 0 || column >= row.Count) return null;
        return row[column] switch
        {
            int i => i,
            short s => s,
            sbyte b => b,
            uint u => checked((int)u),
            _ => null,
        };
    }

    private static float? OptionalFloat(IReadOnlyList<object> row, int column) =>
        column < 0 || column >= row.Count || row[column] is not float value ? null : value;

    private static uint? OptionalUInt(IReadOnlyList<object> row, int column) =>
        column < 0 || column >= row.Count || row[column] is not uint value ? null : value;

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable creature {field}.");
        }

        return value;
    }

    private static int RequireInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not short and not sbyte and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable creature {field}.");
        }

        return row[column] is uint u ? checked((int)u) : Convert.ToInt32(row[column]);
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable creature {field}.");
        }

        return value;
    }
}
