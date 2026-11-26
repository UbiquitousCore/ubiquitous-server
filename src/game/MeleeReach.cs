using Ubiquitous.Common;

namespace Ubiquitous.Game;

public static class MeleeReach
{
    public const double Range = 3.0;

    // Own choice: a chase keeps the chaser's own height where no walkable ground
    // leads up, so reach is judged across the ground with a height allowance that
    // covers a bank a player stands on above a creature, not a cliff.
    public const double Height = 6.0;

    public static bool Reaches(Position from, Position to, double range = Range)
    {
        var dx = from.X - to.X;
        var dz = from.Z - to.Z;
        return (dx * dx) + (dz * dz) <= range * range && Math.Abs(from.Y - to.Y) <= Height;
    }
}
