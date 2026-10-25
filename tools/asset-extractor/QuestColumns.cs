namespace Ubiquitous.AssetExtractor;

public static class QuestColumns
{
    private const string Table = "tables/quests.tbl";

    public static QuestTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        var checks = new List<(int Column, string Types)>
        {
            (Build114.Id, "uk"),
            (Build114.TitleStringId, "u"),
            (Build114.DetailsStringId, "u"),
            (Build114.MinLevel, "u"),
            (Build114.AllowedClassMask, "u"),
        };
        if (Build114.Level >= 0) checks.Add((Build114.Level, "u"));
        if (Build114.QuestFlags >= 0) checks.Add((Build114.QuestFlags, "u"));
        if (Build114.AnyOfQuestsColumn >= 0) checks.Add((Build114.AnyOfQuestsColumn, "s"));
        foreach (int column in Build114.PrerequisitesAll) checks.Add((column, "u"));
        // String ids read as 'k' (key) columns on this build; both read as uints.
        foreach (int column in Build114.ObjectiveStringIds) checks.Add((column, "uk"));
        foreach (int column in Build114.ObjectiveItems) checks.Add((column, "u"));
        foreach (int column in Build114.ObjectiveItemCounts) checks.Add((column, "u"));
        foreach (var group in Build114.ObjectiveCreatureColumns)
            foreach (int column in group) checks.Add((column, "u"));
        foreach (int column in Build114.ObjectiveObjects) checks.Add((column, "u"));
        foreach (int column in Build114.ObjectiveGoalCounts) checks.Add((column, "u"));
        foreach (int column in Build114.ObjectiveSpells) checks.Add((column, "u"));
        TableShape.Expect(Table, format, [.. checks]);
        return Build114;
    }

    public static readonly QuestTableColumns Build114 = new(
        Id: 0,
        GiverEntryId: -1,
        TitleStringId: 24,
        DetailsStringId: 25,
        MinLevel: 4,
        AllowedClassMask: 8,
        Level: 5,

        QuestFlags: 14,
        AnyOfQuestsColumn: 121,
        PrerequisitesAll: [122, 123, 124, 125],
        ObjectiveStringIds: [31, 32, 33, 34],
        ObjectiveItems: [35, 36, 37, 38],
        ObjectiveItemCounts: [39, 40, 41, 42],
        ObjectiveCreatureColumns: [[43, 44, 45, 46], [47, 48, 49, 50], [51, 52, 53, 54], [55, 56, 57, 58]],
        ObjectiveObjects: [59, 60, 61, 62],
        ObjectiveGoalCounts: [63, 64, 65, 66],
        ObjectiveSpells: [67, 68, 69, 70]);
}
