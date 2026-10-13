using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record TravelQuote(uint From, uint To, int Cost, TeleportStopDefinition Destination);

public sealed class TravelService
{

    private readonly IReadOnlyDictionary<uint, TeleportStopDefinition> _stops;
    private readonly Dictionary<uint, Dictionary<uint, int>> _legs = [];

    public TravelService(
        IReadOnlyDictionary<uint, TeleportStopDefinition> stops,
        IReadOnlyList<TravelRouteDefinition> routes)
    {
        _stops = stops;
        var teleportStops = stops.Where(pair => pair.Value.Kind == TeleportStopKind.BusStop)
            .Select(pair => pair.Key)
            .ToHashSet();
        foreach (var id in teleportStops) _legs[id] = [];
        foreach (var route in routes)
        {
            if (!teleportStops.Contains(route.FirstStop) || !teleportStops.Contains(route.SecondStop)) continue;
            AddLeg(route.FirstStop, route.SecondStop, route.Price);
            AddLeg(route.SecondStop, route.FirstStop, route.Price);
        }

        void AddLeg(uint start, uint end, int price)
        {
            var legs = _legs[start];
            if (!legs.TryGetValue(end, out var known) || price < known) legs[end] = price;
        }
    }

    public TravelQuote? Quote(
        uint from,
        uint to,
        ushort playerMapId,
        float playerX,
        float playerY,
        float playerZ)
    {
        if (!_stops.TryGetValue(from, out var departure) || !_stops.TryGetValue(to, out var destination))
            return null;
        if (departure.MapId != playerMapId) return null;

        if (!InteractionRange.WithinHorizontal(
                new Ubiquitous.Common.Position(departure.X, departure.Y, departure.Z),
                new Ubiquitous.Common.Position(playerX, playerY, playerZ),
                InteractionRange.Max))
            return null;

        if (CheapestFare(from, to) is not int cost) return null;

        return new TravelQuote(from, to, cost, destination);
    }

    private int? CheapestFare(uint from, uint to)
    {
        if (!_legs.ContainsKey(from) || !_legs.ContainsKey(to)) return null;
        var best = new Dictionary<uint, int> { [from] = 0 };
        var frontier = new PriorityQueue<uint, int>();
        frontier.Enqueue(from, 0);
        while (frontier.TryDequeue(out var stop, out var spent))
        {
            if (spent > best[stop]) continue;
            if (stop == to) return spent;
            foreach (var (next, price) in _legs[stop])
            {
                var total = spent + price;
                if (best.TryGetValue(next, out var known) && known <= total) continue;
                best[next] = total;
                frontier.Enqueue(next, total);
            }
        }

        return null;
    }
}
