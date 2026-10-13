namespace Ubiquitous.AssetExtractor;

public sealed class CollisionModelLibrary
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<BakeTriangle>?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CollisionModelLibrary(string modelDirectory)
    {
        ArgumentNullException.ThrowIfNull(modelDirectory);
        if (!Directory.Exists(modelDirectory)) return;

        foreach (var path in Directory.EnumerateFiles(modelDirectory, "*.phy", SearchOption.AllDirectories))
        {
            var relative = Normalise(Path.GetRelativePath(modelDirectory, path));
            _files.TryAdd(relative, path);
            FileCount++;

            int firstSeparator = relative.IndexOf('/', StringComparison.Ordinal);
            if (firstSeparator > 0) _files.TryAdd(relative[(firstSeparator + 1)..], path);
        }
    }

    public int FileCount { get; }

    public IReadOnlyList<BakeTriangle>? TrianglesOf(string modelPath)
    {
        if (_cache.TryGetValue(modelPath, out var cached)) return cached;

        IReadOnlyList<BakeTriangle>? triangles = null;
        var exact = Normalise(Path.ChangeExtension(modelPath, ".phy"));
        foreach (var candidate in new[] { exact }.Concat(BaseNames(exact)))
        {
            if (Find(candidate) is not { } file) continue;
            triangles = CollisionMeshReader.ReadFile(file);
            break;
        }

        _cache[modelPath] = triangles;
        return triangles;
    }

    private string? Find(string candidate)
    {
        if (_files.TryGetValue(candidate, out var file)) return file;
        int firstSeparator = candidate.IndexOf('/', StringComparison.Ordinal);
        return firstSeparator > 0 && _files.TryGetValue(candidate[(firstSeparator + 1)..], out file) ? file : null;
    }

    // Own reading of the user's files: scenery is placed as `<model>_s` while its
    // collision ships as `<model>.phy` beside it (41 models in the 1.1.4 extract,
    // town walls among them), and one model is placed with a doubled underscore.
    private static IEnumerable<string> BaseNames(string phyPath)
    {
        var directory = Path.GetDirectoryName(phyPath)?.Replace('\\', '/') ?? "";
        var stem = Path.GetFileNameWithoutExtension(phyPath);
        var single = string.Join('_', stem.Split('_', StringSplitOptions.RemoveEmptyEntries));
        foreach (var name in new[] { single, StripS(stem), StripS(single) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(name, stem, StringComparison.OrdinalIgnoreCase))
                yield return directory.Length == 0 ? $"{name}.phy" : $"{directory}/{name}.phy";
        }
    }

    private static string StripS(string stem) =>
        stem.EndsWith("_s", StringComparison.OrdinalIgnoreCase) ? stem[..^2] : stem;

    private static string Normalise(string path) => path.Replace('\\', '/');
}
