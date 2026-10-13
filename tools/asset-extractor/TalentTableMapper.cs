using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record TalentTableColumns(
    int Id,
    int Tier,
    int Column,
    int Tree,
    int PointsInTreeRequired,
    IReadOnlyList<int> Prerequisites,
    int RankCount,
    IReadOnlyList<int> RankSpells);

public sealed record ClassTreeColumns(int ClassId, IReadOnlyList<int> Trees);

public static class TalentTableMapper
{
    public static IReadOnlyList<TalentTreeDefinition> MapTrees(
        FTableDocument talents,
        FTableDocument classes,
        TalentTableColumns columns,
        ClassTreeColumns classColumns)
    {
        var classByTree = new Dictionary<uint, int>();
        for (int i = 0; i < classes.Rows.Count; i++)
        {
            var row = classes.Rows[i];
            var classId = (int)RequireUInt(row, classColumns.ClassId, i, "class");
            foreach (var column in classColumns.Trees)
            {
                var tree = RequireUInt(row, column, i, $"column {column} (tree)");
                if (tree != 0) classByTree.TryAdd(tree, classId);
            }
        }

        var byTree = new Dictionary<uint, List<TalentDefinition>>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < talents.Rows.Count; i++)
        {
            var row = talents.Rows[i];
            var tree = RequireUInt(row, columns.Tree, i, "tree");
            if (!classByTree.ContainsKey(tree)) continue;

            var id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id)) throw new FTableFormatException($"Duplicate talent id {id}.");

            var rankCount = (int)RequireUInt(row, columns.RankCount, i, "rank count");
            var spells = columns.RankSpells
                .Select(column => RequireUInt(row, column, i, $"column {column} (rank spell)"))
                .Where(spell => spell != 0)
                .ToList();
            if (rankCount < 1 || spells.Count != rankCount)
            {
                throw new FTableFormatException(
                    $"Talent {id} has {rankCount} ranks but names {spells.Count} rank spells.");
            }

            var prerequisites = columns.Prerequisites
                .Select(column => RequireUInt(row, column, i, $"column {column} (prerequisite)"))
                .Where(talent => talent != 0)
                .ToList();

            if (!byTree.TryGetValue(tree, out var list)) byTree[tree] = list = [];
            list.Add(new TalentDefinition(
                id,
                (int)RequireUInt(row, columns.Tier, i, "tier"),
                (int)RequireUInt(row, columns.Column, i, "column"),
                (int)RequireUInt(row, columns.PointsInTreeRequired, i, "points required"),
                prerequisites,
                spells));
        }

        var trees = new List<TalentTreeDefinition>();
        foreach (var (treeId, list) in byTree.OrderBy(pair => pair.Key))
        {
            var ids = list.Select(talent => talent.Id).ToHashSet();
            foreach (var talent in list)
            {
                var missing = talent.Prerequisites.FirstOrDefault(prerequisite => !ids.Contains(prerequisite));
                if (missing != 0)
                {
                    throw new FTableFormatException(
                        $"Talent {talent.Id} needs talent {missing}, which is not on tree {treeId}.");
                }
            }

            var clash = list.GroupBy(talent => (talent.Tier, talent.Column)).FirstOrDefault(group => group.Count() > 1);
            if (clash is not null)
            {
                throw new FTableFormatException(
                    $"Tree {treeId} has two talents at tier {clash.Key.Tier}, column {clash.Key.Column}.");
            }

            trees.Add(new TalentTreeDefinition(
                treeId,
                classByTree[treeId],
                [.. list.OrderBy(talent => talent.Tier).ThenBy(talent => talent.Column)]));
        }

        return trees;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable talent {field}.");
        }

        return value;
    }
}
