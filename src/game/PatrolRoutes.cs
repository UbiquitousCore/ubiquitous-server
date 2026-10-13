using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class PatrolRoutes
{
    public const float Radius = 12f;

    public const float MinStopDistance = 4f;

    public const int MaxOuterStops = 5;

    private const int MinOuterStops = 1;
    private const int PlacementTries = 4;
    private const int MinPauseMs = 2000;
    private const int MaxPauseMs = 6000;

    public static WaypointRouteDefinition Generate(
        CreatureSpawnDefinition spawn,
        Func<Position, Position, bool>? canWalkBetween = null,
        Func<float, float, float?, GroundAnswer?>? groundHeight = null)
    {
        var canWalk = canWalkBetween ?? ((_, _) => true);
        var random = new SpawnRandom(spawn.Id);
        var home = new Position(spawn.X, spawn.Y, spawn.Z);

        var stops = new List<WaypointRouteStop> { new(home.X, home.Y, home.Z, random.Pause()) };
        var previous = home;
        var outerCount = random.Between(MinOuterStops, MaxOuterStops);
        var turn = random.Unit() * 2 * Math.PI;
        var sector = 2 * Math.PI / outerCount;

        for (var index = 0; index < outerCount; index++)
        {
            for (var attempt = 0; attempt < PlacementTries; attempt++)
            {
                var angle = turn + sector * (index + random.Unit());
                var distance = MinStopDistance + random.Unit() * (Radius - MinStopDistance);
                var x = (float)(home.X + Math.Cos(angle) * distance);
                var z = (float)(home.Z + Math.Sin(angle) * distance);
                var y = groundHeight?.Invoke(x, z, home.Y)?.Y ?? home.Y;
                var candidate = new Position(x, y, z);
                if (!canWalk(previous, candidate)) continue;

                stops.Add(new WaypointRouteStop(x, y, z, random.Pause()));
                previous = candidate;
                break;
            }
        }

        while (stops.Count > 1 && !canWalk(previous, home))
        {
            stops.RemoveAt(stops.Count - 1);
            var last = stops[^1];
            previous = new Position(last.X, last.Y, last.Z);
        }

        return new WaypointRouteDefinition(
            spawn.PatrolRouteId, spawn.MapId, stops, LoopToStopIndex: stops.Count > 1 ? 0 : null);
    }

    private sealed class SpawnRandom(uint seed)
    {
        private ulong _state = 0x9E3779B97F4A7C15UL ^ seed;

        public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));

        public int Between(int min, int max) => min + (int)(Next() % (ulong)(max - min + 1));

        public int Pause() => Between(MinPauseMs / 500, MaxPauseMs / 500) * 500;

        private ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
