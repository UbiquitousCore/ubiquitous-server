using System.Buffers.Binary;
using Ubiquitous.Common;

namespace Ubiquitous.AssetExtractor;

public static class CollisionMeshReader
{
    private const int MeshKind = 7;

    private const int ShapeHeaderBytes = 36;

    // Blocks with flag bit 0 or 1 are volumes a body walks into (a building's
    // "inside" box), not against; ubiquitous-client leaves them out of solid collision too.
    private const uint TriggerVolumeBits = 0x3;

    public static IReadOnlyList<BakeTriangle> ReadTriangles(ReadOnlySpan<byte> data)
    {
        var triangles = new List<BakeTriangle>();
        if (data.Length < 4) return triangles;

        int shapes = BinaryPrimitives.ReadInt32LittleEndian(data);
        int offset = 4;
        for (int shape = 0; shape < shapes; shape++)
        {
            if (offset + ShapeHeaderBytes > data.Length) break;

            int kind = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            if (kind == MeshKind)
            {
                if (!ReadMesh(data, ref offset, triangles)) break;
                continue;
            }

            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            float yaw = ReadFloat(data, offset + 8);
            var centre = ReadPoint(data, offset + 12);
            var half = ReadPoint(data, offset + 24);
            offset += ShapeHeaderBytes;
            if ((flags & TriggerVolumeBits) != 0) continue;
            AddBox(triangles, centre, Abs(half), yaw);
        }

        return triangles;
    }

    private static Position ReadPoint(ReadOnlySpan<byte> data, int at) =>
        new(ReadFloat(data, at), ReadFloat(data, at + 8), -ReadFloat(data, at + 4));

    private static Position Abs(Position half) =>
        new(MathF.Abs(half.X), MathF.Abs(half.Y), MathF.Abs(half.Z));

    public static IReadOnlyList<BakeTriangle> ReadFile(string path) => ReadTriangles(File.ReadAllBytes(path));

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    private static bool ReadMesh(ReadOnlySpan<byte> data, ref int offset, List<BakeTriangle> triangles)
    {
        var at = ReadPoint(data, offset + 8);
        int vertexCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 32)..]);
        int triangleCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 34)..]);
        int body = offset + ShapeHeaderBytes;
        if (body + (vertexCount * 12) + (triangleCount * 8) > data.Length) return false;

        var vertices = new Position[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var local = ReadPoint(data, body + (i * 12));
            vertices[i] = new Position(local.X + at.X, local.Y + at.Y, local.Z + at.Z);
        }

        int faces = body + (vertexCount * 12);
        for (int i = 0; i < triangleCount; i++)
        {
            int face = faces + (i * 8);
            int a = BinaryPrimitives.ReadUInt16LittleEndian(data[face..]);
            int b = BinaryPrimitives.ReadUInt16LittleEndian(data[(face + 2)..]);
            int c = BinaryPrimitives.ReadUInt16LittleEndian(data[(face + 4)..]);
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;

            triangles.Add(new BakeTriangle(vertices[a], vertices[b], vertices[c]));
        }

        offset = faces + (triangleCount * 8);
        return true;
    }

    private static void AddBox(List<BakeTriangle> triangles, Position centre, Position half, float yaw)
    {
        var corners = new Position[8];
        for (int i = 0; i < 8; i++)
        {
            var local = new Position(
                (i & 1) == 0 ? -half.X : half.X,
                (i & 2) == 0 ? -half.Y : half.Y,
                (i & 4) == 0 ? -half.Z : half.Z);
            var turned = TurnAboutVertical(local, yaw);
            corners[i] = new Position(centre.X + turned.X, centre.Y + turned.Y, centre.Z + turned.Z);
        }

        AddQuad(triangles, corners[0], corners[2], corners[3], corners[1]);
        AddQuad(triangles, corners[4], corners[5], corners[7], corners[6]);
        AddQuad(triangles, corners[0], corners[1], corners[5], corners[4]);
        AddQuad(triangles, corners[2], corners[6], corners[7], corners[3]);
        AddQuad(triangles, corners[0], corners[4], corners[6], corners[2]);
        AddQuad(triangles, corners[1], corners[3], corners[7], corners[5]);
    }

    private static void AddQuad(List<BakeTriangle> triangles, Position a, Position b, Position c, Position d)
    {
        triangles.Add(new BakeTriangle(a, b, c));
        triangles.Add(new BakeTriangle(a, c, d));
    }

    private static Position TurnAboutVertical(Position point, float yaw)
    {
        if (yaw == 0f) return point;

        float sin = MathF.Sin(yaw);
        float cos = MathF.Cos(yaw);
        return new Position((point.X * cos) + (point.Z * sin), point.Y, (point.Z * cos) - (point.X * sin));
    }
}
