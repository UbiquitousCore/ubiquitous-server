using System.Text;
using System.Text.RegularExpressions;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

// What each playable body can look like, found in the build the way ubiquitous-client
// finds it: a skin, face or hair colour is offered when its image ships, a hair
// style when the body's model carries a mesh for it.
public static partial class PlayerLookReader
{
    public static readonly IReadOnlyList<string> TexturePacks = ["res.bin", "res_t.bin"];

    private static readonly string[] TextureExtensions = [".tga", ".png", ".pvr"];

    // Well past any shipped body, so a run ends with the build's own data.
    private const int Ceiling = 24;

    private const int DisplayModelColumn = 1;
    private const int ModelPathColumn = 2;

    // Each body's model path, from `creaturedisplayinfo` (column 1 names the
    // `creaturemodeldata` row) and `creaturemodeldata` (column 2 its path).
    public static IReadOnlyDictionary<(int Race, int Gender), string> BodyModels(
        FTableDocument displays, FTableDocument models, IEnumerable<(int Race, int Gender, int DisplayInfoId)> bodies)
    {
        var modelOf = displays.Rows.Where(row => row.Count > DisplayModelColumn)
            .GroupBy(row => Convert.ToInt64(row[0]))
            .ToDictionary(group => group.Key, group => Convert.ToInt64(group.First()[DisplayModelColumn]));
        var pathOf = models.Rows.Where(row => row.Count > ModelPathColumn && row[ModelPathColumn] is string)
            .GroupBy(row => Convert.ToInt64(row[0]))
            .ToDictionary(group => group.Key, group => (string)group.First()[ModelPathColumn]);
        var found = new Dictionary<(int, int), string>();
        foreach (var (race, gender, display) in bodies)
        {
            if (modelOf.TryGetValue(display, out var model) && pathOf.TryGetValue(model, out var path) && path.Length > 0)
                found[(race, gender)] = path;
        }

        return found;
    }

    // The name textures are drawn by: a body model's file name without its
    // extension or a one-letter suffix (`human_m_h.bdae` draws as `human_m`).
    public static string StemOf(string modelPath)
    {
        var name = Path.GetFileNameWithoutExtension(modelPath.Replace('\\', '/').Split('/')[^1]);
        return name.Length > 2 && name[^2] == '_' ? name[..^2] : name;
    }

    public static PlayerLookDefinition Read(
        int race, int gender, string modelPath, ISet<string> textureNames, byte[]? model)
    {
        var stem = StemOf(modelPath).ToLowerInvariant();
        IReadOnlyList<int> Found(Func<string, string> name) =>
            [.. Enumerable.Range(1, Ceiling).Where(option => textureNames.Contains(name($"{option:00}")))];

        var skins = Found(n => $"{stem}_body_{n}");
        var first = skins.Count > 0 ? skins[0] : 1;
        var faces = Found(n => $"{stem}_body_{first:00}_face_{n}");
        var hairColours = Found(n => $"{stem}_hair_a_{n}");
        return new PlayerLookDefinition(race, gender, skins, faces, HairStylesIn(model), hairColours);
    }

    // The hair styles a model carries: each mesh named `hair_<n>`.
    public static IReadOnlyList<int> HairStylesIn(byte[]? model)
    {
        if (model is null) return [];
        var text = Encoding.Latin1.GetString(model);
        return [.. HairMesh().Matches(text).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().Order()];
    }

    // Every texture's file name in the packs, lowered, without its extension.
    public static HashSet<string> TextureNames(IEnumerable<string> entries)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var lower = entry.Replace('\\', '/').ToLowerInvariant();
            var extension = TextureExtensions.FirstOrDefault(lower.EndsWith);
            if (extension is null) continue;
            names.Add(lower.Split('/')[^1][..^extension.Length]);
        }

        return names;
    }

    // Writes the bodies' look options into a package beside its other datasets.
    public static int Extract(string build, string tablesDirectory, string packageDirectory, TextWriter log)
    {
        var displays = FTableDocument.Load(File.ReadAllBytes(Path.Combine(tablesDirectory, "creaturedisplayinfo.tbl")));
        var models = FTableDocument.Load(File.ReadAllBytes(Path.Combine(tablesDirectory, "creaturemodeldata.tbl")));
        var bodies = BodyModels(displays, models, PlayerBodies.All);

        var packs = BuildPacks.Open(build, TexturePacks);
        try
        {
            if (packs.Count == 0)
            {
                log.WriteLine($"No texture pack ({string.Join(", ", TexturePacks)}) in {build}.");
                return 1;
            }

            var textures = TextureNames(packs.SelectMany(pack => pack.Archive.Names));
            var looks = new List<PlayerLookDefinition>();
            foreach (var ((race, gender), path) in bodies.OrderBy(pair => pair.Key))
            {
                var bytes = packs.Select(pack => pack.Archive.Names.FirstOrDefault(name =>
                        string.Equals(name.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase)) is { } entry
                        ? pack.Archive.Read(entry) : null)
                    .FirstOrDefault(found => found is not null);
                var look = Read(race, gender, path, textures, bytes);
                looks.Add(look);
                log.WriteLine($"  race {race} gender {gender} ({StemOf(path)}): {look.Skins.Count} skins, {look.Faces.Count} faces, " +
                    $"{look.HairStyles.Count} hair styles, {look.HairColours.Count} hair colours");
            }

            DataPackageWriter.WriteDataset(packageDirectory, PlayerLookDefinition.Dataset, looks);
            log.WriteLine($"{looks.Count} bodies' looks in {packageDirectory}.");
            return 0;
        }
        finally
        {
            foreach (var pack in packs) pack.Dispose();
        }
    }

    [GeneratedRegex(@"(?i)\bhair_(\d+)")]
    private static partial Regex HairMesh();
}
