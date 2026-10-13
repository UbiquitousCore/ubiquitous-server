namespace Ubiquitous.AssetExtractor;

public static class QuestColumns
{
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
