namespace Ubiquitous.AssetExtractor;

public static class CollisionPlacementColumns
{
    public const int SpawnScale = 11;

    public const int ProtoDisplay = 3;

    public const int DisplayModel = 1;
}

public static class CollisionPlacementReader
{
    public static IReadOnlyDictionary<ushort, List<CollisionPlacement>> ReadByMap(string tablesDirectory)
    {
        var spawns = LoadTable(tablesDirectory, "staticobject_spawns.tbl");
        var protos = LoadTable(tablesDirectory, "staticobject_proto.tbl");
        var displays = LoadTable(tablesDirectory, "staticobject_displayinfo.tbl");

        var modelOfDisplay = new Dictionary<uint, string>();
        foreach (var (key, row) in displays.ByKey)
        {
            if (Cell(row, CollisionPlacementColumns.DisplayModel) is string model && model.Length > 0)
            {
                modelOfDisplay[key] = model;
            }
        }

        var displayOfEntry = new Dictionary<uint, uint>();
        foreach (var (key, row) in protos.ByKey)
        {
            if (Cell(row, CollisionPlacementColumns.ProtoDisplay) is uint display) displayOfEntry[key] = display;
        }

        var columns = StaticObjectColumns.Build114;
        var byMap = new Dictionary<ushort, List<CollisionPlacement>>();
        foreach (var row in spawns.Rows)
        {
            if (Cell(row, columns.MapId) is not uint mapId || mapId > ushort.MaxValue) continue;
            if (Cell(row, columns.EntryId) is not uint entry) continue;
            if (!displayOfEntry.TryGetValue(entry, out uint display)) continue;
            if (!modelOfDisplay.TryGetValue(display, out var model)) continue;

            if (Cell(row, columns.X) is not float x) continue;
            if (Cell(row, columns.Y) is not float y) continue;
            if (Cell(row, columns.Z) is not float z) continue;

            float yaw = Cell(row, columns.Yaw) as float? ?? 0f;
            float scale = Cell(row, CollisionPlacementColumns.SpawnScale) as float? ?? 1f;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw)) continue;
            if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;

            if (!byMap.TryGetValue((ushort)mapId, out var placements))
            {
                placements = [];
                byMap[(ushort)mapId] = placements;
            }

            placements.Add(new CollisionPlacement(model, new Ubiquitous.Common.Position(x, y, z), yaw, scale));
        }

        return byMap;
    }

    private static object? Cell(IReadOnlyList<object> row, int column) =>
        column >= 0 && column < row.Count ? row[column] : null;

    private static FTableDocument LoadTable(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            throw new FTableFormatException($"No {name} in {directory} — run the extract step first.");
        }

        return FTableDocument.Load(File.ReadAllBytes(path));
    }
}
