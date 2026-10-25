namespace Ubiquitous.AssetExtractor;

public static class CreatureSpawnColumns
{
    private const string Table = "tables/creature_spawns.tbl";

    public static CreatureSpawnTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.MapId, "u"),
            (Build114.CreatureEntryId, "u"),
            (Build114.X, "f"),
            (Build114.Y, "f"),
            (Build114.Z, "f"),
            (Build114.Yaw, "f"));
        return Build114 with
        {
            IdleBehaviour = TableShape.Optional(Table, format, Build114.IdleBehaviour, "c"),
            PatrolRouteId = TableShape.Optional(Table, format, Build114.PatrolRouteId, "u"),
            WanderRadius = TableShape.Optional(Table, format, Build114.WanderRadius, "f"),
            Leader = TableShape.Optional(Table, format, Build114.Leader, "u"),
            Animation = TableShape.Optional(Table, format, Build114.Animation, "u"),
        };
    }

    public static readonly CreatureSpawnTableColumns Build114 = new(
        Id: 0,
        MapId: 2,
        CreatureEntryId: 1,
        X: 3,
        Y: 4,
        Z: 5,
        Yaw: 6,
        IdleBehaviour: 7,
        Animation: 14,
        PatrolRouteId: 26,
        WanderRadius: 27,
        Leader: 34);
}
