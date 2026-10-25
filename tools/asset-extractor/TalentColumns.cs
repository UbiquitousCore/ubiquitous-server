namespace Ubiquitous.AssetExtractor;

public static class TalentColumns
{
    private const string TalentTable = "tables/spell_talent.tbl";
    private const string ClassTable = "tables/character_class.tbl";

    // 2.2.0 spreads each rank over five columns (spell, then four the
    // import does not read): rank spells at 9, 14, 19, 24 and 29.
    // Verified row by row: every one of the 320 rows names exactly its
    // rank count of nonzero spells there.
    public static readonly TalentTableColumns Build220 = new(
        Id: 0,
        Tier: 1,
        Column: 2,
        Tree: 3,
        PointsInTreeRequired: 4,
        Prerequisites: [5, 6],
        RankCount: 8,
        RankSpells: [9, 14, 19, 24, 29]);

    public static (TalentTableColumns Talents, ClassTreeColumns Classes) Resolve(
        FTableDocument talents, FTableDocument classes)
    {
        string talentFormat = talents.Header.ColumnTypes;
        string classFormat = classes.Header.ColumnTypes;
        if (TableSchemas.Matches(TalentTable, talentFormat, "1.1.4")
            && TableSchemas.Matches(ClassTable, classFormat, "1.1.4"))
        {
            return (Build114, ClassBuild114);
        }

        if (TableSchemas.Matches(TalentTable, talentFormat, "2.2.0")
            && TableSchemas.Matches(ClassTable, classFormat, "2.2.0"))
        {
            return (Build220, ClassBuild114);
        }

        TableShape.Expect(TalentTable, talentFormat,
            (Build114.Id, "uk"),
            (Build114.Tier, "u"),
            (Build114.Column, "u"),
            (Build114.Tree, "u"),
            (Build114.PointsInTreeRequired, "u"),
            (Build114.Prerequisites[0], "u"),
            (Build114.Prerequisites[1], "u"),
            (Build114.RankCount, "u"),
            (Build114.RankSpells[0], "u"),
            (Build114.RankSpells[1], "u"),
            (Build114.RankSpells[2], "u"),
            (Build114.RankSpells[3], "u"),
            (Build114.RankSpells[4], "u"));
        foreach (int tree in ClassBuild114.Trees) TableShape.Expect(ClassTable, classFormat, (tree, "u"));
        TableShape.Expect(ClassTable, classFormat, (ClassBuild114.ClassId, "uk"));
        return (Build114, ClassBuild114);
    }

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
