using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

internal sealed class RouteWalk
{
    public int NextStop;
    public bool Walking;
    public bool Finished;
    public Position Origin;
    public DateTimeOffset StartedAt;
    public DateTimeOffset ArrivesAt;
    public DateTimeOffset ResumeAt;
}

public sealed record RouteUpdate(Position Position, bool Moving, Position? Target, bool IsTransition);

public sealed class CreatureWaypointRouteService
{
    public const double Speed = CreatureWanderService.Speed;

    private readonly Func<float, float, float?, GroundAnswer?> _groundHeight;
    private readonly Dictionary<string, RouteWalk> _walks = new(StringComparer.Ordinal);

    public CreatureWaypointRouteService(Func<float, float, float?, GroundAnswer?>? groundHeight = null)
    {
        _groundHeight = groundHeight ?? ((_, _, _) => null);
    }

    public void Clear(string targetId) => _walks.Remove(targetId);

    // A walk broken off (a fight) starts again from wherever the creature then stands.
    public void Halt(string targetId, DateTimeOffset now)
    {
        if (!_walks.TryGetValue(targetId, out var walk) || !walk.Walking) return;
        walk.Walking = false;
        walk.ResumeAt = now;
    }

    // Where a creature partway to its next stop stands at this moment, without moving it on.
    public Position? PositionAt(string targetId, WaypointRouteDefinition route, DateTimeOffset now)
    {
        if (!_walks.TryGetValue(targetId, out var walk) || !walk.Walking || walk.Finished
            || walk.NextStop >= route.Stops.Count)
            return null;
        var destination = Positioned(route.Stops[walk.NextStop]);
        var totalSeconds = (walk.ArrivesAt - walk.StartedAt).TotalSeconds;
        var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - walk.StartedAt).TotalSeconds / totalSeconds, 0.0, 1.0);
        var x = (float)(walk.Origin.X + (destination.X - walk.Origin.X) * t);
        var z = (float)(walk.Origin.Z + (destination.Z - walk.Origin.Z) * t);
        var y = (float)(walk.Origin.Y + (destination.Y - walk.Origin.Y) * t);
        return new Position(x, GroundY(x, z, Math.Max(y, walk.Origin.Y), y), z);
    }

    public RouteUpdate? Advance(string targetId, Position current, WaypointRouteDefinition route, DateTimeOffset now)
    {
        if (route.Stops.Count == 0) return null;

        if (!_walks.TryGetValue(targetId, out var walk))
        {
            _walks[targetId] = new RouteWalk { NextStop = 0, ResumeAt = now };
            return null;
        }

        if (walk.Finished) return null;

        var destination = Positioned(route.Stops[walk.NextStop]);

        if (!walk.Walking)
        {
            if (now < walk.ResumeAt) return null;

            var distance = GroundDistance(current, destination);
            if (distance <= 0)
            {
                return Arrive(walk, route, destination, now, current.Y);
            }

            walk.Origin = current;
            walk.Walking = true;
            walk.StartedAt = now;
            walk.ArrivesAt = now + TimeSpan.FromSeconds(distance / Speed);
            return new RouteUpdate(current, Moving: true, Target: destination, IsTransition: true);
        }

        if (now >= walk.ArrivesAt)
        {
            return Arrive(walk, route, destination, now, current.Y);
        }

        var totalSeconds = (walk.ArrivesAt - walk.StartedAt).TotalSeconds;
        var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - walk.StartedAt).TotalSeconds / totalSeconds, 0.0, 1.0);
        var x = (float)(walk.Origin.X + (destination.X - walk.Origin.X) * t);
        var z = (float)(walk.Origin.Z + (destination.Z - walk.Origin.Z) * t);
        var y = (float)(walk.Origin.Y + (destination.Y - walk.Origin.Y) * t);
        var interpolated = new Position(x, GroundY(x, z, Math.Max(y, walk.Origin.Y), y), z);
        return new RouteUpdate(interpolated, Moving: true, Target: destination, IsTransition: false);
    }

    private RouteUpdate Arrive(
        RouteWalk walk, WaypointRouteDefinition route, Position destination, DateTimeOffset now, float nearY)
    {
        var reached = route.Stops[walk.NextStop];
        walk.Walking = false;

        var pause = Math.Max(0, reached.PauseMilliseconds);
        walk.ResumeAt = now + TimeSpan.FromMilliseconds(pause);

        var following = walk.NextStop + 1;
        if (following < route.Stops.Count)
        {
            walk.NextStop = following;
        }
        else if (route.LoopToStopIndex is { } loopTo && loopTo >= 0 && loopTo < route.Stops.Count)
        {
            walk.NextStop = loopTo;
        }
        else
        {
            walk.Finished = true;
        }

        var grounded = new Position(destination.X, GroundY(destination.X, destination.Z, nearY, destination.Y), destination.Z);
        return new RouteUpdate(grounded, Moving: false, Target: null, IsTransition: true);
    }

    private static Position Positioned(WaypointRouteStop stop) => new(stop.X, stop.Y, stop.Z);

    private float GroundY(float x, float z, float fallback) =>
        _groundHeight(x, z, fallback)?.Y ?? fallback;

    // The mesh reaches far down but only a step up, so the lookup hint errs high while the filed value stays the fallback.
    private float GroundY(float x, float z, float nearY, float fallback) =>
        _groundHeight(x, z, nearY)?.Y ?? fallback;

    private static double GroundDistance(Position from, Position to)
    {
        double dx = to.X - from.X;
        double dz = to.Z - from.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
