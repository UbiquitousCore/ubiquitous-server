using System.IO.Compression;

namespace Ubiquitous.Data;

public sealed record NavigationParameters(
    float CellSize,
    float BodyRadius,
    float StepHeight,
    float MaxSlopeDegrees,
    float Headroom);

// A square of the map under water deep enough to matter: one terrain chunk and its surface height.
public sealed record NavigationWater(float MinX, float MinZ, float Size, float Surface);

public sealed record NavigationPackageFile(
    ushort MapId,
    NavigationParameters Parameters,
    byte[] Mesh,
    IReadOnlyList<NavigationWater>? Water = null)
{
    public IReadOnlyList<NavigationWater> Water { get; init; } = Water ?? [];
}

public static class NavigationAreas
{
    // Ground under water deep enough to swim: still crossable, but dearer to route across.
    public const int Swim = 2;

    // A body swims once the bed lies this far below the surface; ubiquitous-client uses the same depth.
    public const float SwimDepth = 1.25f;

    // Swimming is half running speed, and players keep to land when the way round is not long.
    public const float SwimCost = 4f;
}

public static class NavigationPackage
{
    public const string DirectoryName = "navigation";

    public const ushort FormatVersion = 3;

    // Version 2 carried no water; it still reads, as a map with none.
    private const ushort WithoutWater = 2;

    private static readonly byte[] MagicBytes = "UBQWALK"u8.ToArray();

    public static string FileName(ushort mapId) => $"{mapId}.nav";

    public static string OverrideFileName(ushort mapId) => $"{mapId}.overrides.json";

    public static void Write(Stream stream, NavigationPackageFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        stream.Write(MagicBytes, 0, MagicBytes.Length);

        using (var header = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            header.Write(FormatVersion);
            header.Write(file.MapId);
            header.Write(file.Parameters.CellSize);
            header.Write(file.Parameters.BodyRadius);
            header.Write(file.Parameters.StepHeight);
            header.Write(file.Parameters.MaxSlopeDegrees);
            header.Write(file.Parameters.Headroom);
            header.Write(file.Mesh.Length);

            // Ahead of the mesh: a deflate stream reads past its own end.
            header.Write(file.Water.Count);
            foreach (var square in file.Water)
            {
                header.Write(square.MinX);
                header.Write(square.MinZ);
                header.Write(square.Size);
                header.Write(square.Surface);
            }

            header.Flush();
        }

        using var deflate = new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        deflate.Write(file.Mesh, 0, file.Mesh.Length);
    }

    public static NavigationPackageFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var magic = new byte[MagicBytes.Length];
        stream.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Not a walkable-ground file, or written by a different format version.");
        }

        ushort version;
        ushort mapId;
        var squares = new List<NavigationWater>();
        NavigationParameters parameters;
        int meshBytes;
        using (var header = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            version = header.ReadUInt16();
            if (version != FormatVersion && version != WithoutWater)
            {
                throw new InvalidDataException(
                    $"Walkable ground states format version {version}; this build reads {FormatVersion}. Re-bake the map.");
            }

            mapId = header.ReadUInt16();
            parameters = new NavigationParameters(
                header.ReadSingle(), header.ReadSingle(), header.ReadSingle(),
                header.ReadSingle(), header.ReadSingle());
            meshBytes = header.ReadInt32();
            if (version != WithoutWater)
            {
                int count = header.ReadInt32();
                if (count < 0) throw new InvalidDataException($"Walkable ground states {count} squares of water.");
                for (int i = 0; i < count; i++)
                    squares.Add(new NavigationWater(header.ReadSingle(), header.ReadSingle(), header.ReadSingle(), header.ReadSingle()));
            }
        }

        if (meshBytes < 0) throw new InvalidDataException($"Walkable ground states a mesh of {meshBytes} bytes.");

        var mesh = new byte[meshBytes];
        using (var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true))
        {
            deflate.ReadExactly(mesh);
        }

        return new NavigationPackageFile(mapId, parameters, mesh, squares);
    }
}
