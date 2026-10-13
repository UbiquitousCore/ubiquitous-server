using Ubiquitous.Common;

namespace Ubiquitous.Game;

public static class InteractionRange
{
    public const double Max = 10.0;

    public static bool Within(Position from, Position to, double range = Max) =>
        from.DistanceSquaredTo(to) <= range * range;

    public static bool WithinHorizontal(Position from, Position to, double range = Max)
    {
        var dx = from.X - to.X;
        var dz = from.Z - to.Z;
        return (dx * dx) + (dz * dz) <= range * range;
    }
}
