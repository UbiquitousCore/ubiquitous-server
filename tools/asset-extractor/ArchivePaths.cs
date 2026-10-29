namespace Ubiquitous.AssetExtractor;

// Where an archive member may be written. A member's name comes from the
// archive, and a tampered build can name a path outside the output directory
// (absolute, or climbing with "..") to overwrite one of the user's own files;
// such a name gets no destination at all.
public static class ArchivePaths
{
    // The member's path inside `root`, or null when its name would leave it.
    public static string? Inside(string root, string entryName)
    {
        var relative = entryName.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        if (relative.Length == 0 || Path.IsPathRooted(relative)) return null;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return destination.StartsWith(fullRoot, StringComparison.Ordinal) ? destination : null;
    }

    // Every member's destination, checked before anything is written, or the
    // first name that would leave `root`: a tampered archive is refused whole.
    public static (Dictionary<string, string>? Destinations, string? Refused) AllInside(
        string root, IEnumerable<string> entryNames)
    {
        var destinations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in entryNames)
        {
            if (Inside(root, name) is not { } destination) return (null, name);
            destinations[name] = destination;
        }

        return (destinations, null);
    }
}
