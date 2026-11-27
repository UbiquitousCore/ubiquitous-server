namespace Ubiquitous.AssetExtractor;

public static class TravelRouteColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/t_bus_route.tbl",
        "uuuuu",
        [
            LayoutColumn.Must(nameof(TravelRouteTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(TravelRouteTableColumns.FirstStop), 1, "u"),
            LayoutColumn.Must(nameof(TravelRouteTableColumns.SecondStop), 2, "u"),
            LayoutColumn.Must(nameof(TravelRouteTableColumns.Price), 3, "u"),
        ],
        // 1.0.3 to 1.1.1
        new LayoutRevision("uuuu", new LayoutEdit.Lacks(4)),
        // 4.2.21
        new LayoutRevision("uuuuuu", new LayoutEdit.Adds(5)));

    public static TravelRouteTableColumns Reference => Layout.Reference.As<TravelRouteTableColumns>();
}
