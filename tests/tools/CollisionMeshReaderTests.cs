using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class CollisionMeshReaderTests
{
    private static byte[] Block(
        float yaw, float centreX, float centreY, float centreZ, float halfX, float halfY, float halfZ, uint flags = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(3);
        writer.Write(flags);
        writer.Write(yaw);
        WriteWorldPoint(writer, centreX, centreY, centreZ);
        WriteWorldPoint(writer, halfX, halfY, halfZ);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteWorldPoint(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x);
        writer.Write(-z);
        writer.Write(y);
    }

    [Fact]
    public void ABlock_ComesOutAsTwelveTrianglesAroundItsOwnCentre()
    {
        var triangles = CollisionMeshReader.ReadTriangles(
            Block(yaw: 0f, centreX: 2f, centreY: 1f, centreZ: 3f, halfX: 1f, halfY: 1f, halfZ: 0.5f));

        Assert.Equal(12, triangles.Count);

        var xs = triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).ToList();
        var ys = triangles.SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).ToList();
        var zs = triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToList();

        Assert.Equal(1f, xs.Min(), precision: 4);
        Assert.Equal(3f, xs.Max(), precision: 4);

        Assert.Equal(0f, ys.Min(), precision: 4);
        Assert.Equal(2f, ys.Max(), precision: 4);
        Assert.Equal(2.5f, zs.Min(), precision: 4);
        Assert.Equal(3.5f, zs.Max(), precision: 4);
    }

    [Theory]
    [InlineData(0x1u)]
    [InlineData(0x2u)]
    [InlineData(0x4002u)]
    public void ABlockFlaggedAsAVolumeToWalkInto_IsNotSolid(uint flags)
    {
        Assert.Empty(CollisionMeshReader.ReadTriangles(
            Block(yaw: 0f, centreX: 0f, centreY: 5f, centreZ: 0f, halfX: 10f, halfY: 5f, halfZ: 10f, flags)));
    }

    [Fact]
    public void AWoodenBlock_IsStillSolid()
    {
        Assert.Equal(12, CollisionMeshReader.ReadTriangles(
            Block(yaw: 0f, centreX: 0f, centreY: 1f, centreZ: 0f, halfX: 1f, halfY: 1f, halfZ: 1f, flags: 0x400)).Count);
    }

    [Fact]
    public void ABlockTurnedAQuarterTurn_SwapsWhichWayRoundItIsWide()
    {
        var triangles = CollisionMeshReader.ReadTriangles(
            Block(yaw: MathF.PI / 2f, centreX: 0f, centreY: 1f, centreZ: 0f, halfX: 2f, halfY: 1f, halfZ: 0.5f));

        var xs = triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).ToList();
        var zs = triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToList();

        Assert.Equal(0.5f, xs.Max(), precision: 3);
        Assert.Equal(2f, zs.Max(), precision: 3);
    }

    [Fact]
    public void ABlockWithANegativeHalfSize_IsStillABlockOfThatSize()
    {
        var triangles = CollisionMeshReader.ReadTriangles(
            Block(yaw: 0f, centreX: 0f, centreY: 0f, centreZ: 0f, halfX: -2f, halfY: 1f, halfZ: 1f));

        var xs = triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).ToList();
        Assert.Equal(-2f, xs.Min(), precision: 4);
        Assert.Equal(2f, xs.Max(), precision: 4);
    }

    [Fact]
    public void AMesh_ComesOutAsItsOwnTrianglesStoodWhereTheMeshStands()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(7);
            writer.Write(0);
            WriteWorldPoint(writer, 1f, 4f, 0f);
            WriteWorldPoint(writer, 2f, 4f, 2f);
            writer.Write((ushort)3);
            writer.Write((ushort)1);
            foreach (var (x, y, z) in new[] { (0f, 0f, 0f), (2f, 0f, 0f), (0f, 0f, 2f) })
            {
                WriteWorldPoint(writer, x, y, z);
            }

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)2);
            writer.Write((ushort)0);
        }

        var triangles = CollisionMeshReader.ReadTriangles(stream.ToArray());

        var one = Assert.Single(triangles);
        Assert.Equal(1f, one.A.X, precision: 4);
        Assert.Equal(4f, one.A.Y, precision: 4);
        Assert.Equal(0f, one.A.Z, precision: 4);
        Assert.Equal(3f, one.B.X, precision: 4);
        Assert.Equal(2f, one.C.Z, precision: 4);
    }

    [Fact]
    public void TwoShapesInOneFile_BothComeOutWhereTheyWereWritten()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(0);
            writer.Write(0f);
            WriteWorldPoint(writer, 0f, 0f, 0f);
            WriteWorldPoint(writer, 1f, 1f, 1f);

            writer.Write(3);
            writer.Write(0);
            writer.Write(0f);
            WriteWorldPoint(writer, 20f, 0f, 0f);
            WriteWorldPoint(writer, 1f, 1f, 1f);
        }

        var triangles = CollisionMeshReader.ReadTriangles(stream.ToArray());

        Assert.Equal(24, triangles.Count);
        var xs = triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).ToList();
        Assert.Equal(-1f, xs.Min(), precision: 4);
        Assert.Equal(21f, xs.Max(), precision: 4);
    }

    [Fact]
    public void AFileThatStopsEarly_KeepsTheShapesItDidCarry()
    {
        var bytes = Block(0f, 0f, 1f, 0f, 1f, 1f, 1f);
        bytes[0] = 4;

        Assert.Equal(12, CollisionMeshReader.ReadTriangles(bytes).Count);
    }

    [Fact]
    public void AnEmptyFile_ComesOutEmptyRatherThanThrowing()
    {
        Assert.Empty(CollisionMeshReader.ReadTriangles([]));
        Assert.Empty(CollisionMeshReader.ReadTriangles([1, 0, 0, 0]));
    }
}
