using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

internal static class BakedGround
{
    public static void AddFloor(
        List<BakeTriangle> into, float minX, float minZ, float maxX, float maxZ, float y)
    {
        var a = new Position(minX, y, minZ);
        var b = new Position(maxX, y, minZ);
        var c = new Position(maxX, y, maxZ);
        var d = new Position(minX, y, maxZ);
        into.Add(new BakeTriangle(a, c, b));
        into.Add(new BakeTriangle(a, d, c));
    }

    public static List<BakeTriangle> Floor(float minX, float minZ, float maxX, float maxZ, float y)
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, minX, minZ, maxX, maxZ, y);
        return triangles;
    }

    public static void AddBox(
        List<BakeTriangle> into, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        var corners = new Position[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = new Position(
                (i & 1) == 0 ? minX : maxX,
                (i & 2) == 0 ? minY : maxY,
                (i & 4) == 0 ? minZ : maxZ);
        }

        void Quad(int a, int b, int c, int d)
        {
            into.Add(new BakeTriangle(corners[a], corners[b], corners[c]));
            into.Add(new BakeTriangle(corners[a], corners[c], corners[d]));
        }

        Quad(2, 6, 7, 3);
        Quad(0, 1, 5, 4);
        Quad(0, 4, 6, 2);
        Quad(1, 3, 7, 5);
        Quad(0, 2, 3, 1);
        Quad(4, 5, 7, 6);
    }

    public static NavigationMesh Mesh(IReadOnlyList<BakeTriangle> triangles, IReadOnlyList<NavigationWater> water)
    {
        var result = NavigationBaker.Bake(new TriangleCollisionSource(triangles), NavigationBaker.DefaultParameters, null, water);
        return new NavigationMesh(result.Parameters, result.Mesh, water);
    }

    public static NavigationMesh Mesh(IReadOnlyList<BakeTriangle> triangles, NavigationOverrides? corrections = null)
    {
        var result = NavigationBaker.Bake(triangles, corrections);
        return new NavigationMesh(result.Parameters, result.Mesh);
    }

    public static void AssertGround(float expected, float? actual)
    {
        Xunit.Assert.NotNull(actual);
        Xunit.Assert.InRange(
            actual!.Value,
            expected - NavigationBaker.CellHeight,
            expected + NavigationBaker.CellHeight);
    }

    public static WalkableGroundSource Source(ushort mapId, IReadOnlyList<BakeTriangle> triangles) =>
        new(new Dictionary<ushort, NavigationMesh> { [mapId] = Mesh(triangles) });
}
