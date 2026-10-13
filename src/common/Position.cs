namespace Ubiquitous.Common;

public readonly record struct Position(float X, float Y, float Z)
{
    public float DistanceSquaredTo(Position other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
