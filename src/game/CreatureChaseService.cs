using Ubiquitous.Common;

namespace Ubiquitous.Game;

public sealed record ChaseUpdate(Position Position, bool Moving, Position? Target);

public sealed class CreatureChaseService
{
    public const double Speed = MovementRules.PlayerForwardSpeed;

    private const double MaxStepSeconds = 1.0;

    public const double RouteRedrawDistance = 2.0;

    private sealed class Route
    {
        public required IReadOnlyList<Position> Corners { get; init; }

        public int Next { get; set; }

        public required Position PlannedFor { get; init; }
    }

    private readonly Dictionary<string, DateTimeOffset> _lastTickAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);
    private readonly Func<Position, Position, IReadOnlyList<Position>?> _route;

    public CreatureChaseService(Func<Position, Position, IReadOnlyList<Position>?>? route = null) =>
        _route = route ?? ((_, _) => null);

    public void Clear(string targetId)
    {
        _lastTickAt.Remove(targetId);
        _routes.Remove(targetId);
    }

    public ChaseUpdate? Advance(
        string targetId, Position current, Position targetPosition, double meleeRange, DateTimeOffset now,
        double pace = 1.0)
    {
        var distanceSquared = current.DistanceSquaredTo(targetPosition);
        var wasChasing = _lastTickAt.ContainsKey(targetId);
        if (distanceSquared <= meleeRange * meleeRange)
        {
            if (!wasChasing) return null;
            Clear(targetId);
            return new ChaseUpdate(current, Moving: false, Target: null);
        }

        var dt = wasChasing
            ? Math.Clamp((now - _lastTickAt[targetId]).TotalSeconds, 0, MaxStepSeconds)
            : 0.0;
        _lastTickAt[targetId] = now;

        var route = RouteFor(targetId, current, targetPosition);
        var (moved, heading) = Walk(route, current, Speed * Math.Max(0, pace) * dt);

        return new ChaseUpdate(moved, Moving: true, Target: heading);
    }

    private Route RouteFor(string chaserId, Position current, Position quarry)
    {
        if (_routes.TryGetValue(chaserId, out var held)
            && held.Next < held.Corners.Count
            && Apart(held.PlannedFor, quarry) <= RouteRedrawDistance)
        {
            return held;
        }

        var found = _route(current, quarry);
        var route = new Route
        {
            Corners = found ?? [new Position(quarry.X, current.Y, quarry.Z)],
            Next = 0,
            PlannedFor = quarry,
        };
        _routes[chaserId] = route;
        return route;
    }

    private static (Position At, Position Heading) Walk(Route route, Position from, double budget)
    {
        var at = from;
        while (route.Next < route.Corners.Count)
        {
            var corner = route.Corners[route.Next];
            double dx = corner.X - at.X, dz = corner.Z - at.Z;
            var leg = Math.Sqrt((dx * dx) + (dz * dz));
            if (leg <= 0)
            {
                route.Next++;
                continue;
            }

            if (budget < leg)
            {
                var t = budget / leg;
                return (
                    new Position(
                        (float)(at.X + (dx * t)),
                        (float)(at.Y + ((corner.Y - at.Y) * t)),
                        (float)(at.Z + (dz * t))),
                    corner);
            }

            budget -= leg;
            at = corner;
            route.Next++;
        }

        return (at, at);
    }

    private static double Apart(Position a, Position b)
    {
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
