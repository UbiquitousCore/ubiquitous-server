using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record SpellEffectTableColumns(
    int Id, int Type, int SubType, int School, int BaseValue, int Spread, int EffectArgument,
    int Target = -1, int TargetCondition = -1, int CasterCondition = -1);

public static class SpellEffectTableMapper
{
    public static IReadOnlyList<SpellEffectDefinition> MapEffects(
        FTableDocument document, SpellEffectTableColumns columns)
    {
        var effects = new List<SpellEffectDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate spell effect id {id}.");
            }

            effects.Add(new SpellEffectDefinition(
                id,
                RequireInt(row, columns.Type, i, $"column {columns.Type} (effect kind)"),
                RequireInt(row, columns.SubType, i, $"column {columns.SubType} (effect detail)"),
                RequireInt(row, columns.School, i, $"column {columns.School} (school)"),
                RequireInt(row, columns.BaseValue, i, $"column {columns.BaseValue} (amount)"),
                RequireInt(row, columns.Spread, i, $"column {columns.Spread} (spread)"),
                RequireUInt(row, columns.EffectArgument, i, $"column {columns.EffectArgument} (argument)"),
                columns.Target >= 0 ? RequireInt(row, columns.Target, i, $"column {columns.Target} (target)") : 0,
                columns.TargetCondition >= 0
                    ? RequireUInt(row, columns.TargetCondition, i, $"column {columns.TargetCondition} (target condition)") : 0,
                columns.CasterCondition >= 0
                    ? RequireUInt(row, columns.CasterCondition, i, $"column {columns.CasterCondition} (caster condition)") : 0));
        }

        return effects;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable spell effect {field}.");
        }

        return value;
    }

    private static int RequireInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable spell effect {field}.");
        }

        return row[column] is uint u ? unchecked((int)u) : (int)row[column];
    }
}
