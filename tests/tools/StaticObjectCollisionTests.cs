using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;

namespace Ubiquitous.Integration.Tests;

public class StaticObjectCollisionTests
{
    private static IReadOnlyList<BakeTriangle> LopsidedSlab() =>
    [
        new(new Position(0, 0, 2), new Position(0, 0, 6), new Position(2, 0, 6)),
        new(new Position(0, 0, 2), new Position(2, 0, 6), new Position(2, 0, 2)),
    ];

    private static StaticObjectCollisionSource SourceWith(CollisionPlacement placement) =>
        new([placement], _ => LopsidedSlab());

    [Fact]
    public void TrianglesIn_PutsAPlacementOnTheSideOfItsOriginTheWorldDrawsItOn()
    {
        var source = SourceWith(new CollisionPlacement("slab", new Position(100, 0, 100), Yaw: 0, Scale: 1));

        var triangles = source.TrianglesIn(0, 0, 200, 200);

        Assert.NotEmpty(triangles);
        var minZ = triangles.Min(t => MathF.Min(t.A.Z, MathF.Min(t.B.Z, t.C.Z)));
        var maxZ = triangles.Max(t => MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z)));

        Assert.Equal(102f, minZ, precision: 3);
        Assert.Equal(106f, maxZ, precision: 3);
    }

    [Fact]
    public void TrianglesIn_KeepsAFloorFacingUpwards()
    {
        var source = SourceWith(new CollisionPlacement("slab", new Position(0, 0, 0), Yaw: 0, Scale: 1));

        foreach (var triangle in source.TrianglesIn(-50, -50, 50, 50))
        {
            var ux = triangle.B.X - triangle.A.X;
            var uz = triangle.B.Z - triangle.A.Z;
            var vx = triangle.C.X - triangle.A.X;
            var vz = triangle.C.Z - triangle.A.Z;
            Assert.True((vx * uz) - (vz * ux) > 0, "a floor should still face upwards");
        }
    }
}
