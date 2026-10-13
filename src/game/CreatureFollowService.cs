using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

internal sealed class FormationSlot
{
    public double Distance;
    public double Bearing;
    public double SquaredToYaw;
    public bool Placed;
    public bool Moving;
    public DateTimeOffset LastTickAt;
    public Position? LastDestination;
}

public sealed record FollowUpdate(Position Position, bool Moving, Position? Target, bool IsTransition);

public sealed class CreatureFollowService
{
    public const double Speed = CreatureWaypointRouteService.Speed;

    public const double TurnThreshold = Math.PI / 18;

    private const double MaxStepSeconds = 1.0;

    private readonly Func<float, float, float?, GroundAnswer?> _groundHeight;
    private readonly Dictionary<string, FormationSlot> _slots = new(StringComparer.Ordinal);

    public CreatureFollowService(Func<float, float, float?, GroundAnswer?>? groundHeight = null)
    {
        _groundHeight = groundHeight ?? ((_, _, _) => null);
    }

    public void Clear(string followerId) => _slots.Remove(followerId);

    public FollowUpdate? Advance(
        string followerId,
        Position current,
        Position leaderPosition,
        Position? leaderDestination,
        CreatureSpawnDefinition follower,
        CreatureSpawnDefinition leader,
        DateTimeOffset now)
    {
        if (!_slots.TryGetValue(followerId, out var slot))
        {
            slot = NewSlot(follower, leader);
            _slots[followerId] = slot;
        }

        if (TravelYaw(leaderPosition, leaderDestination) is double yaw
            && Math.Abs(AngleBetween(yaw, slot.SquaredToYaw)) > TurnThreshold)
            slot.SquaredToYaw = yaw;

        if (!slot.Placed)
        {
            slot.Placed = true;
            slot.LastTickAt = now;
            return new FollowUpdate(PlaceBeside(slot, leaderPosition, leaderPosition.Y), Moving: false, Target: null, IsTransition: true);
        }

        var anchor = leaderDestination ?? leaderPosition;
        var destination = PlaceBeside(slot, anchor, leaderPosition.Y);
        double dt = Math.Clamp((now - slot.LastTickAt).TotalSeconds, 0, MaxStepSeconds);
        slot.LastTickAt = now;

        double dx = destination.X - current.X;
        double dz = destination.Z - current.Z;
        double distance = Math.Sqrt((dx * dx) + (dz * dz));
        double step = Speed * dt;

        if (distance <= step)
        {
            bool wasMoving = slot.Moving;
            slot.Moving = false;
            slot.LastDestination = null;
            return new FollowUpdate(destination, Moving: false, Target: null, IsTransition: wasMoving);
        }

        double t = step / distance;
        var x = (float)(current.X + (dx * t));
        var z = (float)(current.Z + (dz * t));
        var next = new Position(x, GroundY(x, z, destination.Y), z);

        bool changed = !slot.Moving || slot.LastDestination != destination;
        slot.Moving = true;
        slot.LastDestination = destination;
        return new FollowUpdate(next, Moving: true, Target: destination, IsTransition: changed);
    }

    private static FormationSlot NewSlot(CreatureSpawnDefinition follower, CreatureSpawnDefinition leader)
    {
        double dx = follower.X - leader.X;
        double dz = follower.Z - leader.Z;
        return new FormationSlot
        {
            Distance = Math.Sqrt((dx * dx) + (dz * dz)),
            Bearing = YawOf(dx, dz) - leader.Yaw,
            SquaredToYaw = leader.Yaw,
        };
    }

    private Position PlaceBeside(FormationSlot slot, Position anchor, float nearY)
    {
        double angle = slot.SquaredToYaw + slot.Bearing;
        var x = (float)(anchor.X + (Math.Sin(angle) * slot.Distance));
        var z = (float)(anchor.Z - (Math.Cos(angle) * slot.Distance));
        return new Position(x, GroundY(x, z, nearY, anchor.Y), z);
    }

    private float GroundY(float x, float z, float fallback) =>
        _groundHeight(x, z, fallback)?.Y ?? fallback;

    // The mesh reaches far down but only a step up, so the lookup hint errs high while the filed value stays the fallback.
    private float GroundY(float x, float z, float nearY, float fallback) =>
        _groundHeight(x, z, nearY)?.Y ?? fallback;

    private static double? TravelYaw(Position leaderPosition, Position? leaderDestination)
    {
        if (leaderDestination is not { } destination) return null;
        double dx = destination.X - leaderPosition.X;
        double dz = destination.Z - leaderPosition.Z;
        return (dx * dx) + (dz * dz) > 0 ? YawOf(dx, dz) : null;
    }

    private static double YawOf(double dx, double dz) => Math.Atan2(dx, -dz);

    private static double AngleBetween(double to, double from) =>
        Math.IEEERemainder(to - from, 2 * Math.PI);
}
