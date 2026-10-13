using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Ubiquitous.AssetExtractor;

public static class WorldBoundsReader
{
    private const int UnitsPerTile = 64;

    private static readonly Regex TilePattern =
        new(@"terrain/(-?\d+)_(-?\d+)\.trn$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public readonly record struct Bounds(float MinX, float MaxX, float MinZ, float MaxZ);

    public static Bounds? ComputeBounds(IEnumerable<string> zipEntryNames)
    {
        int? minTileX = null, maxTileX = null, minTileZ = null, maxTileZ = null;
        foreach (var name in zipEntryNames)
        {
            var match = TilePattern.Match(name);
            if (!match.Success) continue;

            int tileX = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int tileZ = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            minTileX = minTileX is null ? tileX : Math.Min(minTileX.Value, tileX);
            maxTileX = maxTileX is null ? tileX : Math.Max(maxTileX.Value, tileX);
            minTileZ = minTileZ is null ? tileZ : Math.Min(minTileZ.Value, tileZ);
            maxTileZ = maxTileZ is null ? tileZ : Math.Max(maxTileZ.Value, tileZ);
        }

        if (minTileX is null || maxTileX is null || minTileZ is null || maxTileZ is null) return null;
        return new Bounds(
            minTileX.Value * UnitsPerTile, (maxTileX.Value + 1) * UnitsPerTile,
            minTileZ.Value * UnitsPerTile, (maxTileZ.Value + 1) * UnitsPerTile);
    }

    public static Bounds? ComputeBoundsFromArchive(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        return ComputeBounds(archive.Entries.Select(e => e.FullName));
    }
}
