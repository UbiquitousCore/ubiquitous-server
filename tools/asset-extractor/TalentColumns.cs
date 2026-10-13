namespace Ubiquitous.AssetExtractor;

public static class TalentColumns
{
    public static readonly TalentTableColumns Build114 = new(
        Id: 0,
        Tier: 1,
        Column: 2,
        Tree: 3,
        PointsInTreeRequired: 4,
        Prerequisites: [5, 6],
        RankCount: 8,
        RankSpells: [9, 10, 11, 12, 13]);

    public static readonly ClassTreeColumns ClassBuild114 = new(ClassId: 0, Trees: [3, 4, 5]);
}
