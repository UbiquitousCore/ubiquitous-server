using System.Reflection;

namespace Ubiquitous.AssetExtractor;

// The builds whose table shapes were captured into fingerprints/, and the
// naming of a tables archive by the one it agrees with.
public static class KnownBuilds
{
    public const string Unknown = "unknown";

    private const string ResourcePrefix = "fingerprints/";

    public static readonly IReadOnlyList<(string Build, TableFingerprint Shapes)> All = Load();

    // The build every table the capture shares with it agrees with on shape.
    // Where several do (a partial archive), the one sharing the most tables
    // and leaving the fewest unshared is named; none agreeing is Unknown.
    public static string Identify(TableFingerprint capture)
    {
        var captured = capture.Tables.Where(t => t.Error is null).ToDictionary(t => t.Name, t => t.Format, StringComparer.Ordinal);
        string best = Unknown;
        (int Shared, int Unshared) bestScore = (0, int.MaxValue);
        foreach (var (build, shapes) in All)
        {
            int shared = 0;
            bool agrees = true;
            foreach (var table in shapes.Tables)
            {
                if (!captured.TryGetValue(table.Name, out var format)) continue;
                if (format != table.Format)
                {
                    agrees = false;
                    break;
                }

                shared++;
            }

            if (!agrees || shared == 0) continue;
            int unshared = shapes.Tables.Count + captured.Count - (2 * shared);
            if (shared > bestScore.Shared || (shared == bestScore.Shared && unshared < bestScore.Unshared))
            {
                best = build;
                bestScore = (shared, unshared);
            }
        }

        return best;
    }

    private static IReadOnlyList<(string, TableFingerprint)> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                string build = Path.GetFileNameWithoutExtension(name[ResourcePrefix.Length..]);
                return (build, TableFingerprint.Load(stream));
            })
            .ToList();
    }
}
