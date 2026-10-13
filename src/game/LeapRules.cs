using Ubiquitous.Common;

namespace Ubiquitous.Game;

public static class LeapRules
{
    public static Position? Landing(Position from, Position target, float standOff)
    {
        var dx = target.X - from.X;
        var dz = target.Z - from.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= standOff) return null;
        var share = (distance - standOff) / distance;
        return new Position(from.X + dx * share, target.Y, from.Z + dz * share);
    }
}
