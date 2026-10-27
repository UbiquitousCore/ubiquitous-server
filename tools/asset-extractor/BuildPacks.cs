namespace Ubiquitous.AssetExtractor;

public sealed class OpenedPack(string name, PackedArchive archive, Stream stream) : IDisposable
{
    public string Name { get; } = name;
    public PackedArchive Archive { get; } = archive;

    public byte[] ReadAllBytes()
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    public void Dispose()
    {
        Archive.Dispose();
        stream.Dispose();
    }
}

// The named packs of a client build: its .ipa, its unpacked app directory, a
// directory holding the packs, or one pack itself. Returned in `names` order.
public static class BuildPacks
{
    public static List<OpenedPack> Open(string build, IReadOnlyList<string> names, TextWriter? log = null)
    {
        var opened = new List<OpenedPack>();
        foreach (var (name, read) in Find(build, names))
        {
            Stream? stream = null;
            try
            {
                stream = read();
                opened.Add(new OpenedPack(name, PackedArchive.Open(stream), stream));
            }
            catch (Exception ex) when (ex is PackedArchiveFormatException or IOException or UnauthorizedAccessException)
            {
                stream?.Dispose();
                log?.WriteLine($"  {name}: {ex.Message} — skipped.");
            }
        }

        return opened;
    }

    private static List<(string Name, Func<Stream> Read)> Find(string build, IReadOnlyList<string> names)
    {
        var found = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(build))
        {
            foreach (var path in Directory.EnumerateFiles(build, "*.bin", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(path);
                if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) found.TryAdd(name, () => File.OpenRead(path));
            }
        }
        else if (File.Exists(build))
        {
            var name = Path.GetFileName(build);
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                found[name] = () => File.OpenRead(build);
            }
            else
            {
                using var archive = PackedArchive.Open(build);
                foreach (var entry in archive.Names)
                {
                    var inner = Path.GetFileName(entry);
                    if (names.Contains(inner, StringComparer.OrdinalIgnoreCase))
                        found.TryAdd(inner, () => ReadInside(build, entry));
                }
            }
        }

        // The pack keeps the name the build gives it, whatever case it was asked for in.
        return [.. names.Where(found.ContainsKey).Select(name => (found.Keys.First(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)), found[name]))];
    }

    // Writes one named pack (usually tables.bin) out of a build — its .ipa,
    // its unpacked app directory, a directory holding the packs, or the pack
    // itself — into outDirectory, so later steps read a plain file.
    // Overwrites: a newer build replaces what an older one left.
    public static int CopyPack(string build, string pack, string outDirectory, TextWriter log)
    {
        var opened = Open(build, [pack], log);
        try
        {
            if (opened.Count == 0)
            {
                log.WriteLine($"No pack '{pack}' in {build}.");
                return 1;
            }

            Directory.CreateDirectory(outDirectory);
            var destination = Path.Combine(outDirectory, opened[0].Name);
            File.WriteAllBytes(destination, opened[0].ReadAllBytes());
            var size = new FileInfo(destination).Length;
            var shown = size < 1024 ? $"{size} bytes" : $"{size / 1024} KiB";
            log.WriteLine($"{opened[0].Name} ({shown}) in {outDirectory}.");
            return 0;
        }
        finally
        {
            foreach (var openedPack in opened) openedPack.Dispose();
        }
    }

    private static MemoryStream ReadInside(string build, string entry)
    {
        using var archive = PackedArchive.Open(build);
        return new MemoryStream(archive.Read(entry), writable: false);
    }
}
