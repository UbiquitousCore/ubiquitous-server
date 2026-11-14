namespace Ubiquitous.AssetExtractor;

public static class MapInfoColumns
{
    public static readonly MapTableColumns Build114 = new(
        Id: 0,
        InternalName: 9,
        File: 10,
        RecallX: 5, RecallY: 6, RecallZ: 7, RecallMapId: 8,
        Kind: 3, MinLevel: 4);

    // 4.2.2 adds three floats after the level (a point on the dungeon's own
    // map, not read), moving the put-out point, its map, name and file by three.
    public static readonly MapTableColumns Build422 = Build114 with
    {
        InternalName = 12,
        File = 13,
        RecallX = 8,
        RecallY = 9,
        RecallZ = 10,
        RecallMapId = 11,
    };

    public static MapTableColumns For(TableLayout layout) => layout.Cols == 21 ? Build422 : Build114;

    // The build whose column layouts a package was read with, for its manifest.
    // TODO: Verify the 21-column shape against other builds; it is measured on 4.2.2 only, and 2.2.0 may share it.
    public static string BuildOf(TableLayout layout) => For(layout) == Build422 ? "4.2.2" : "1.1.4";
}
