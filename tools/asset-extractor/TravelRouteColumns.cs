namespace Ubiquitous.AssetExtractor;

public static class TravelRouteColumns
{
    private const string Table = "tables/t_bus_route.tbl";

    public static TravelRouteTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.FirstStop, "u"),
            (Build114.SecondStop, "u"),
            (Build114.Price, "u"));
        return Build114;
    }

    public static readonly TravelRouteTableColumns Build114 = new(
        Id: 0,
        FirstStop: 1,
        SecondStop: 2,
        Price: 3);
}
