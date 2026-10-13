using Ubiquitous.Common;

namespace Ubiquitous.Game;

public readonly record struct GroundAnswer(float Y, bool KnowsFloors);

internal sealed class WanderLeg
{
    public Position Origin;
    public Position? Destination;
    public DateTimeOffset StartedAt;
    public DateTimeOffset ArrivesAt;
    public DateTimeOffset ResumeAt;
}

public sealed record WanderUpdate(Position Position, bool Moving, Position? Target, bool IsTransition);

public sealed class CreatureWanderService
{
    public static readonly TimeSpan PauseDuration = TimeSpan.FromSeconds(6);

    public static readonly TimeSpan ShortestPause = TimeSpan.FromSeconds(2);

    public const double Speed = MovementRules.PlayerForwardSpeed * MovementRules.WalkSpeedRatio;

    private const double MinLegSeconds = 1.0;

    private const double MinLegDistance = 0.1;

    private readonly Func<double> _rollUnit;
    private readonly Func<float, float, float?, GroundAnswer?> _groundHeight;
    private readonly Dictionary<string, WanderLeg> _legs = new(StringComparer.Ordinal);

    public CreatureWanderService(
        Func<double>? rollUnit = null,
        Func<float, float, float?, GroundAnswer?>? groundHeight = null,
        Func<Position, Position, bool>? canWalkBetween = null)
    {
        _rollUnit = rollUnit ?? (() => Random.Shared.NextDouble());
        _groundHeight = groundHeight ?? ((_, _, _) => null);
        _canWalkBetween = canWalkBetween ?? ((_, _) => true);
    }

    private readonly Func<Position, Position, bool> _canWalkBetween;

    private const int Attempts = 6;

    public void Clear(string targetId) => _legs.Remove(targetId);

    // Where a creature partway along a leg stands at this moment, without moving it on.
    public Position? PositionAt(string targetId, DateTimeOffset now)
    {
        if (!_legs.TryGetValue(targetId, out var leg) || leg.Destination is not { } destination) return null;
        var totalSeconds = (leg.ArrivesAt - leg.StartedAt).TotalSeconds;
        var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - leg.StartedAt).TotalSeconds / totalSeconds, 0.0, 1.0);
        var x = (float)(leg.Origin.X + (destination.X - leg.Origin.X) * t);
        var z = (float)(leg.Origin.Z + (destination.Z - leg.Origin.Z) * t);
        return new Position(x, GroundY(x, z, (float)(leg.Origin.Y + (destination.Y - leg.Origin.Y) * t)), z);
    }

    public WanderUpdate? Advance(string targetId, Position current, Position home, double wanderRadius, DateTimeOffset now)
    {
        if (!_legs.TryGetValue(targetId, out var leg))
        {
            _legs[targetId] = new WanderLeg { ResumeAt = now + FirstPause() };
            return null;
        }

        if (leg.Destination is not { } destination)
        {
            if (now < leg.ResumeAt) return null;

            var chosen = PickReachableDestination(home, wanderRadius);
            if (chosen is null) return null;
            var picked = chosen.Value;
            var distance = GroundDistance(current, picked);
            if (distance < MinLegDistance)
            {
                leg.ResumeAt = now + NextPause();
                return null;
            }

            var travelSeconds = Math.Max(MinLegSeconds, distance / Speed);
            leg.Origin = current;
            leg.Destination = picked;
            leg.StartedAt = now;
            leg.ArrivesAt = now + TimeSpan.FromSeconds(travelSeconds);
            return new WanderUpdate(current, Moving: true, Target: picked, IsTransition: true);
        }

        if (now >= leg.ArrivesAt)
        {
            leg.Destination = null;
            leg.ResumeAt = now + NextPause();
            return new WanderUpdate(destination, Moving: false, Target: null, IsTransition: true);
        }

        var totalSeconds = (leg.ArrivesAt - leg.StartedAt).TotalSeconds;
        var t = totalSeconds <= 0 ? 1.0 : Math.Clamp((now - leg.StartedAt).TotalSeconds / totalSeconds, 0.0, 1.0);
        var x = (float)(leg.Origin.X + (destination.X - leg.Origin.X) * t);
        var z = (float)(leg.Origin.Z + (destination.Z - leg.Origin.Z) * t);
        return new WanderUpdate(
            new Position(x, GroundY(x, z, home.Y), z), Moving: true, Target: destination, IsTransition: false);
    }

    private TimeSpan FirstPause() => PauseDuration * _rollUnit();

    private TimeSpan NextPause() => ShortestPause + ((PauseDuration - ShortestPause) * _rollUnit());

    private float GroundY(float x, float z, float fallback) =>
        _groundHeight(x, z, fallback)?.Y ?? fallback;

    private static double GroundDistance(Position from, Position to)
    {
        double dx = to.X - from.X;
        double dz = to.Z - from.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private Position? PickReachableDestination(Position home, double wanderRadius)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = PickDestination(home, wanderRadius);
            if (_canWalkBetween(home, candidate)) return candidate;
        }

        return null;
    }

    private Position PickDestination(Position home, double wanderRadius)
    {
        var bearing = _rollUnit() * 2.0 * Math.PI;
        var distance = Math.Sqrt(_rollUnit()) * wanderRadius;
        var x = home.X + (float)(Math.Cos(bearing) * distance);
        var z = home.Z + (float)(Math.Sin(bearing) * distance);
        return new Position(x, GroundY(x, z, home.Y), z);
    }
}
