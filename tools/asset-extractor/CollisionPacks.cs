namespace Ubiquitous.AssetExtractor;

// Where a build keeps its collision models. ubiquitous-client looks a placed model's
// `.phy` up in these packs, best first, ignoring case; the packs holding them
// differ between builds and the paths inside do not.
public static class CollisionPacks
{
    public static readonly IReadOnlyList<string> Names = ["model_phy.bin", "tables.bin", "res.bin"];

    // Each `.phy` path, case folded, with the index of the first pack that holds it.
    public static IReadOnlyDictionary<string, int> Choose(IReadOnlyList<IEnumerable<string>> entriesByPack)
    {
        var chosen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int pack = 0; pack < entriesByPack.Count; pack++)
        {
            foreach (var entry in entriesByPack[pack])
            {
                if (entry.EndsWith(".phy", StringComparison.OrdinalIgnoreCase)) chosen.TryAdd(entry, pack);
            }
        }

        return chosen;
    }

    // Writes every collision model the build's packs hold under `outDirectory`
    // at its path in the pack, the best pack's copy where several hold one. A
    // model already there is kept: the map's own build comes first, and another
    // build only fills what it lacks.
    // `source` is the client build: its .ipa, its unpacked app directory, or a
    // directory holding the packs themselves.
    public static int Extract(string source, string outDirectory, TextWriter log)
    {
        var opened = BuildPacks.Open(source, Names, log);
        try
        {
            if (opened.Count == 0)
            {
                log.WriteLine($"No collision pack ({string.Join(", ", Names)}) in {source}.");
                return 1;
            }

            var chosen = Choose([.. opened.Select(pack => (IEnumerable<string>)pack.Archive.Names)]);
            var (destinations, refused) = ArchivePaths.AllInside(outDirectory, chosen.Keys);
            if (destinations is null)
            {
                log.WriteLine($"Refusing {source}: entry \"{refused}\" would be written outside {outDirectory}. The file may have been tampered with.");
                return 1;
            }

            var written = new int[opened.Count];
            int kept = 0;
            foreach (var (entry, pack) in chosen)
            {
                var destination = destinations[entry];
                if (File.Exists(destination))
                {
                    kept++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, opened[pack].Archive.Read(entry));
                written[pack]++;
            }

            for (int pack = 0; pack < opened.Count; pack++)
                log.WriteLine($"  {opened[pack].Name}: {written[pack]} collision models");
            log.WriteLine($"{chosen.Count - kept} collision models added to {outDirectory}, {kept} already there kept.");
            return chosen.Count > 0 ? 0 : 1;
        }
        finally
        {
            foreach (var pack in opened) pack.Dispose();
        }
    }
}
