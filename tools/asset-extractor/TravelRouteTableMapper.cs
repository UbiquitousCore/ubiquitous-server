using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record TravelRouteTableColumns(int Id, int FirstStop, int SecondStop, int Price);

public static class TravelRouteTableMapper
{
    public static IReadOnlyList<TravelRouteDefinition> MapTravelRoutes(
        FTableDocument document,
        TravelRouteTableColumns columns,
        IReadOnlySet<uint> stopIds)
    {
        var routes = new List<TravelRouteDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate travel route id {id}.");
            }

            uint firstStop = RequireUInt(row, columns.FirstStop, i, $"column {columns.FirstStop} (first stop)");
            uint secondStop = RequireUInt(row, columns.SecondStop, i, $"column {columns.SecondStop} (second stop)");
            uint price = RequireUInt(row, columns.Price, i, $"column {columns.Price} (price)");

            if (!stopIds.Contains(firstStop))
            {
                throw new FTableFormatException($"Travel route {id} references missing stop {firstStop}.");
            }

            if (!stopIds.Contains(secondStop))
            {
                throw new FTableFormatException($"Travel route {id} references missing stop {secondStop}.");
            }

            if (firstStop == secondStop)
            {
                throw new FTableFormatException($"Travel route {id} joins stop {firstStop} to itself.");
            }

            routes.Add(new TravelRouteDefinition(id, firstStop, secondStop, unchecked((int)price)));
        }

        return routes;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable travel route {field}.");
        }

        return value;
    }
}
