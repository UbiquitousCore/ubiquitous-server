namespace Ubiquitous.AssetExtractor;

public sealed record QuestBindingTableColumns(int Entry, int Quest);

public sealed record QuestItemBindingTableColumns(int Entry, int Quest, int Item, int Reserve);

// Who starts and finishes a quest: four tables of one shape, and the
// objects that hand over a quest's item.
public static class QuestBindingColumns
{
    public static readonly ColumnLayout CreatureStarter = Binding("tables/creature_quest_starter.tbl");

    public static readonly ColumnLayout CreatureFinisher = Binding("tables/creature_quest_finisher.tbl");

    public static readonly ColumnLayout StaticObjectStarter = Binding("tables/staticobject_quest_starter.tbl");

    public static readonly ColumnLayout StaticObjectFinisher = Binding("tables/staticobject_quest_finisher.tbl");

    public static readonly ColumnLayout StaticObjectItem = new(
        "tables/staticobject_quest_item_binding.tbl",
        "uuuu",
        [
            LayoutColumn.Must(nameof(QuestItemBindingTableColumns.Entry), 0, "uk"),
            LayoutColumn.Must(nameof(QuestItemBindingTableColumns.Quest), 1, "u"),
            LayoutColumn.Must(nameof(QuestItemBindingTableColumns.Item), 2, "u"),
            LayoutColumn.Must(nameof(QuestItemBindingTableColumns.Reserve), 3, "u"),
        ]);

    private static ColumnLayout Binding(string table) => new(
        table,
        "uu",
        [
            LayoutColumn.Must(nameof(QuestBindingTableColumns.Entry), 0, "uk"),
            LayoutColumn.Must(nameof(QuestBindingTableColumns.Quest), 1, "u"),
        ]);
}
