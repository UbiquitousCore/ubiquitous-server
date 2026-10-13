using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Ubiquitous.Common;

namespace Ubiquitous.AssetExtractor;

public readonly record struct PlacementTurn(float X, float Y, float Z, float W);

public readonly record struct PlacementScale(float X, float Y, float Z);

public sealed record WorldItemPlacement(string Model, Position Position, PlacementTurn Turn, PlacementScale Scale);

public sealed record WorldItemTile(int TileX, int TileZ, IReadOnlyList<WorldItemPlacement> Placements);

public static class WorldItemReader
{
    private const int HeaderBytes = 24;
    private const int ResourceIndexAt = 12;
    private const int PositionAt = 16;
    private const int TurnAt = 28;
    private const int ScaleAt = 44;

    private static readonly byte[] Magic = "AITM"u8.ToArray();

    private static readonly int[] KnownStrides = [56, 64];

    private static readonly Regex TilePattern =
        new(@"items/(-?\d+)_(-?\d+)\.itm$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<WorldItemTile> ReadTiles(PackedArchive archive, out int unreadable)
    {
        ArgumentNullException.ThrowIfNull(archive);

        int skipped = 0;
        var tiles = new List<WorldItemTile>();
        foreach (var name in archive.Names.Where(name => TilePattern.IsMatch(name)).OrderBy(name => name, StringComparer.Ordinal))
        {
            byte[] bytes;
            try
            {
                bytes = archive.Read(name);
            }
            catch (PackedArchiveFormatException)
            {
                skipped++;
                continue;
            }

            var tile = ReadTile(bytes);
            if (tile is null)
            {
                skipped++;
                continue;
            }

            tiles.Add(tile);
        }

        unreadable = skipped;
        return tiles;
    }

    public static WorldItemTile? ReadTile(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderBytes) return null;
        if (!data[..4].SequenceEqual(Magic)) return null;

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        int tileX = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        int tileZ = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int placements = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        int resources = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);

        if (placements == 0) return new WorldItemTile(tileX, tileZ, []);
        if (placements < 0 || resources <= 0) return null;

        foreach (int stride in StridesToTry(version))
        {
            var names = ReadResourceNames(data, placements, resources, stride);
            if (names is null) continue;

            var read = new List<WorldItemPlacement>(placements);
            for (int i = 0; i < placements; i++)
            {
                int at = HeaderBytes + (i * stride);
                uint resource = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + ResourceIndexAt)..]);
                read.Add(new WorldItemPlacement(
                    names[(int)resource],
                    new Position(
                        ReadFloat(data, at + PositionAt),
                        ReadFloat(data, at + PositionAt + 4),
                        ReadFloat(data, at + PositionAt + 8)),
                    new PlacementTurn(
                        ReadFloat(data, at + TurnAt),
                        ReadFloat(data, at + TurnAt + 4),
                        ReadFloat(data, at + TurnAt + 8),
                        ReadFloat(data, at + TurnAt + 12)),
                    new PlacementScale(
                        ReadFloat(data, at + ScaleAt),
                        ReadFloat(data, at + ScaleAt + 4),
                        ReadFloat(data, at + ScaleAt + 8))));
            }

            return new WorldItemTile(tileX, tileZ, read);
        }

        return null;
    }

    public static (int X, int Z)? TileOfEntry(string entryName)
    {
        var match = TilePattern.Match(entryName ?? string.Empty);
        return match.Success
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
               int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private static IEnumerable<int> StridesToTry(uint version)
    {
        int stated = version switch { 3 => 56, 4 => 64, _ => 0 };
        if (stated != 0) yield return stated;
        foreach (int stride in KnownStrides)
        {
            if (stride != stated) yield return stride;
        }
    }

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    private static string[]? ReadResourceNames(ReadOnlySpan<byte> data, int placements, int resources, int stride)
    {
        long recordsEnd = (long)HeaderBytes + ((long)placements * stride) + 4;
        long offsetsEnd = recordsEnd + ((long)resources * 4);
        if (offsetsEnd > data.Length) return null;

        var ends = new int[resources];
        for (int i = 0; i < resources; i++)
        {
            ends[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(recordsEnd + (i * 4))..]);
        }

        var blob = data[(int)offsetsEnd..];

        if (ends[^1] > blob.Length) return null;

        var names = new string[resources];
        int start = 0;
        for (int i = 0; i < resources; i++)
        {
            int end = ends[i];
            if (end < start || end > blob.Length) return null;

            int stop = start;
            while (stop < end && blob[stop] != 0) stop++;

            var name = new char[stop - start];
            for (int at = start; at < stop; at++)
            {
                byte c = blob[at];
                if (c < 32 || c >= 127) return null;

                name[at - start] = (char)c;
            }

            var text = new string(name);
            if (text.Length == 0 || !text.Contains('/', StringComparison.Ordinal)) return null;

            names[i] = text;
            start = end;
        }

        for (int i = 0; i < placements; i++)
        {
            uint resource = BinaryPrimitives.ReadUInt32LittleEndian(data[(HeaderBytes + (i * stride) + ResourceIndexAt)..]);
            if (resource >= (uint)resources) return null;
        }

        return names;
    }
}
