namespace Ubiquitous.AssetExtractor;

public static class CreatureSpawnColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/creature_spawns.tbl",
        "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.CreatureEntryId), 1, "u"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.MapId), 2, "u"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.X), 3, "f"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.Y), 4, "f"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.Z), 5, "f"),
            LayoutColumn.Must(nameof(CreatureSpawnTableColumns.Yaw), 6, "f"),
            LayoutColumn.May(nameof(CreatureSpawnTableColumns.IdleBehaviour), 7, "c"),
            LayoutColumn.May(nameof(CreatureSpawnTableColumns.Animation), 14, "u"),
            LayoutColumn.May(nameof(CreatureSpawnTableColumns.PatrolRouteId), 26, "u"),
            LayoutColumn.May(nameof(CreatureSpawnTableColumns.WanderRadius), 27, "f"),
            LayoutColumn.May(nameof(CreatureSpawnTableColumns.Leader), 34, "u"),
        ],
        // 1.0.0 and 1.0.1
        new LayoutRevision("uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuu", new LayoutEdit.Lacks(35, 7)));

    public static CreatureSpawnTableColumns Reference => Layout.Reference.As<CreatureSpawnTableColumns>();
}
