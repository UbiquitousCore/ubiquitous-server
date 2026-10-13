using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public readonly record struct NavigationLink(Position From, Position To);

public static class StaircaseLinks
{
    private const float SameStep = 0.05f;

    private const float TallestRise = 4f;

    private const int FewestSteps = 3;

    private const float LeastRunPerClimb = 0.5f;

    public static NavigationLink? Of(
        string model, IReadOnlyList<BakeTriangle> triangles, NavigationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsNamedAsAFlight(model)) return null;

        var steps = Steps(triangles, parameters.MaxSlopeDegrees);
        if (steps.Count < FewestSteps) return null;

        float climb = steps[^1].Y - steps[0].Y;
        if (climb <= parameters.StepHeight) return null;

        for (int i = 1; i < steps.Count; i++)
        {
            if (steps[i].Y - steps[i - 1].Y > TallestRise) return null;
        }

        var foot = steps[0];
        var head = steps[^1];
        float dx = head.X - foot.X;
        float dz = head.Z - foot.Z;
        float run = MathF.Sqrt((dx * dx) + (dz * dz));
        if (run < climb * LeastRunPerClimb) return null;

        float beyond = parameters.BodyRadius + parameters.CellSize;
        float alongX = dx / run;
        float alongZ = dz / run;
        float rise = steps[1].Y - steps[0].Y;
        return new NavigationLink(
            new Position(foot.X - (alongX * beyond), foot.Y - rise, foot.Z - (alongZ * beyond)),
            new Position(head.X + (alongX * beyond), head.Y, head.Z + (alongZ * beyond)));
    }

    private static bool IsNamedAsAFlight(string model) =>
        model.Contains("stair", StringComparison.OrdinalIgnoreCase)
        || model.Contains("step", StringComparison.OrdinalIgnoreCase);

    private static List<Position> Steps(IReadOnlyList<BakeTriangle> triangles, float maxSlopeDegrees)
    {
        float upright = MathF.Cos(maxSlopeDegrees * MathF.PI / 180f);
        var flat = new List<Position>();
        foreach (var triangle in triangles)
        {
            if (UprightNess(triangle) < upright) continue;
            flat.Add(new Position(
                (triangle.A.X + triangle.B.X + triangle.C.X) / 3f,
                (triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3f,
                (triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3f));
        }

        flat.Sort((one, other) => one.Y.CompareTo(other.Y));

        var steps = new List<Position>();
        int from = 0;
        while (from < flat.Count)
        {
            int to = from;
            while (to + 1 < flat.Count && flat[to + 1].Y - flat[from].Y <= SameStep) to++;

            float x = 0f, y = 0f, z = 0f;
            for (int i = from; i <= to; i++)
            {
                x += flat[i].X;
                y += flat[i].Y;
                z += flat[i].Z;
            }

            int count = to - from + 1;
            steps.Add(new Position(x / count, y / count, z / count));
            from = to + 1;
        }

        return steps;
    }

    private static float UprightNess(BakeTriangle t)
    {
        float ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z;
        float vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
        float nx = (uy * vz) - (uz * vy);
        float ny = (uz * vx) - (ux * vz);
        float nz = (ux * vy) - (uy * vx);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return length <= 0f ? 0f : ny / length;
    }
}
