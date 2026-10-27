namespace Ubiquitous.AssetExtractor;

// What an operator dropped in: a whole client build (the .ipa, the unpacked
// app, or a folder of its packs) or tables.bin alone. Only a whole build
// carries the maps, collision models and looks.
public sealed record IngestSource(string DropDirectory, string Build, string? Tables, bool FullBuild)
{
    public static IngestSource? Resolve(string source)
    {
        if (File.Exists(source))
        {
            var drop = Path.GetDirectoryName(Path.GetFullPath(source))!;
            if (Path.GetFileName(source).Equals("tables.bin", StringComparison.OrdinalIgnoreCase))
            {
                var (build, full) = FindBuild(drop) ?? (drop, false);
                return new IngestSource(drop, build, source, full);
            }

            return new IngestSource(drop, source, null, source.EndsWith(".ipa", StringComparison.OrdinalIgnoreCase));
        }

        if (Directory.Exists(source))
        {
            var (build, full) = FindBuild(source) ?? (source, false);
            return new IngestSource(source, build, FindFirst(source, "tables.bin"), full);
        }

        return null;
    }

    public static string? FindFirst(string directory, string pattern)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
        };
        return Directory.EnumerateFiles(directory, pattern, options)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (string Build, bool Full)? FindBuild(string directory)
    {
        if (FindFirst(directory, "*.ipa") is { } ipa) return (ipa, true);
        var pack = FindFirst(directory, "model_phy.bin") ?? FindFirst(directory, "res.bin");
        return pack is null ? null : (Path.GetDirectoryName(pack)!, true);
    }
}
