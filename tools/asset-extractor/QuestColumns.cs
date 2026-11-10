namespace Ubiquitous.AssetExtractor;

// One table, two views: the quest itself and what it pays.
public static class QuestColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/quests.tbl",
        "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu",
        [
            LayoutColumn.Must(nameof(QuestTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.QuestId), 0, "uk"),
            LayoutColumn.Must(nameof(QuestTableColumns.MinLevel), 4, "u"),
            LayoutColumn.Must(nameof(QuestTableColumns.Level), 5, "u"),
            LayoutColumn.Must(nameof(QuestTableColumns.AllowedClassMask), 8, "u"),
            LayoutColumn.Must(nameof(QuestTableColumns.QuestFlags), 14, "u"),
            LayoutColumn.Must(nameof(QuestTableColumns.TitleStringId), 24, "uk"),
            LayoutColumn.Must(nameof(QuestTableColumns.DetailsStringId), 25, "uk"),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveStringIds), "uk", 31, 32, 33, 34),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveItems), "u", 35, 36, 37, 38),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveItemCounts), "u", 39, 40, 41, 42),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveCreatureColumns) + "[0]", "u", 43, 44, 45, 46),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveCreatureColumns) + "[1]", "u", 47, 48, 49, 50),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveCreatureColumns) + "[2]", "u", 51, 52, 53, 54),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveCreatureColumns) + "[3]", "u", 55, 56, 57, 58),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveObjects), "u", 59, 60, 61, 62),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveGoalCounts), "u", 63, 64, 65, 66),
            LayoutColumn.MustRun(nameof(QuestTableColumns.ObjectiveSpells), "u", 67, 68, 69, 70),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.ChoiceItemStart), 75, "u"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.ChoiceCountStart), 81, "u"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.FixedItemStart), 87, "u"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.FixedCountStart), 91, "u"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.Money), 108, "iu"),
            LayoutColumn.Must(nameof(QuestRewardTableColumns.Xp), 109, "iu"),
            LayoutColumn.Must(nameof(QuestTableColumns.AnyOfQuestsColumn), 121, "s"),
            LayoutColumn.MustRun(nameof(QuestTableColumns.PrerequisitesAll), "u", 122, 123, 124, 125),
        ],
        // 4.2.21
        new LayoutRevision("uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuuu", new LayoutEdit.Adds(167)));

    public static QuestTableColumns Reference => Layout.Reference.As<QuestTableColumns>();

    public static QuestRewardTableColumns RewardReference => Layout.Reference.As<QuestRewardTableColumns>();
}
