using System.Text.Json;

namespace Ubiquitous.Data.Tests;

public class DataManifestTests
{
    private static DataManifest Sample() => new(
        ExtractorVersion: "1.0.0",
        GameVersion: "1.0.2",
        SourceHash: "abc123",
        GeneratedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Datasets: new Dictionary<string, string>
        {
            ["maps"] = "maps.json",
            ["spells"] = "spells.json",
            ["creatures"] = "creatures.json",
            ["creatureSpawns"] = "creature-spawns.json",
            ["staticObjectSpawns"] = "static-object-spawns.json",
        });

    [Fact]
    public void Manifest_JsonRoundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ubiquitous-manifest-{Guid.NewGuid():N}.json");
        try
        {
            DataManifestJson.Save(Sample(), path);
            var loaded = DataManifestJson.Load(path);

            Assert.Equal("1.0.2", loaded.GameVersion);
            Assert.Equal("maps.json", loaded.Datasets["maps"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Catalog_Validate_PassesWithAllRequiredDatasets()
    {
        var result = new DataCatalog(Sample()).Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Catalog_Validate_FailsWhenRequiredDatasetMissing()
    {
        var manifest = Sample() with { Datasets = new Dictionary<string, string> { ["maps"] = "maps.json" } };

        var result = new DataCatalog(manifest).Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("spells"));
        Assert.Contains(result.Errors, e => e.Contains("creatures"));
    }

    [Fact]
    public void Catalog_LoadFromDirectory_MissingManifest_Throws()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Throws<FileNotFoundException>(() => DataCatalog.LoadFromDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void Catalog_LoadFromDirectory_RequiresListedDatasetFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            DataManifestJson.Save(Sample(), Path.Combine(dir, DataManifest.FileName));
            File.WriteAllText(Path.Combine(dir, "maps.json"), "[]");
            File.WriteAllText(Path.Combine(dir, "spells.json"), "[]");

            var result = DataCatalog.LoadFromDirectory(dir).Validate();

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("creatures"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_RejectsDatasetPathOutsideRoot()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = Sample() with
            {
                Datasets = new Dictionary<string, string>
                {
                    ["maps"] = "../outside.json",
                    ["spells"] = "spells.json",
                    ["creatures"] = "creatures.json",
                },
            };
            DataManifestJson.Save(manifest, Path.Combine(dir, DataManifest.FileName));

            var result = DataCatalog.LoadFromDirectory(dir).Validate();

            Assert.Contains(result.Errors, error => error.Contains("escapes the data directory"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_LoadsMapsAndCreaturesFromNormalizedJson()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures: [new CreatureDefinition(12, "wolf", 3, 80)]);

            var catalog = DataCatalog.LoadFromDirectory(dir);

            Assert.Equal("starter", Assert.Single(catalog.LoadMaps()).InternalName);
            Assert.Equal(80, Assert.Single(catalog.LoadCreatures()).MaxHealth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_RejectsDuplicateCreatureIds()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures:
                [
                    new CreatureDefinition(12, "wolf", 3, 80),
                    new CreatureDefinition(12, "wolf-alt", 4, 90),
                ]);

            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadCreatures());

            Assert.Contains("duplicate id 12", error.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_LoadsCreatureSpawnsWithResolvedReferences()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures: [new CreatureDefinition(12, "wolf", 3, 80)],
                creatureSpawns: [new CreatureSpawnDefinition(101, 7, 12, 1, 2, 3, 0.5f)]);

            var spawn = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadCreatureSpawns());

            Assert.Equal((uint)12, spawn.CreatureEntryId);
            Assert.Equal(3, spawn.Z);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_RejectsCreatureSpawnWithMissingTemplate()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures: [new CreatureDefinition(12, "wolf", 3, 80)],
                creatureSpawns: [new CreatureSpawnDefinition(101, 7, 99, 1, 2, 3, 0)]);

            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadCreatureSpawns());

            Assert.Contains("missing creature 99", error.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_LoadsStaticObjectSpawnsWithResolvedMap()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures: [new CreatureDefinition(12, "wolf", 3, 80)],
                staticObjectSpawns: [new StaticObjectSpawnDefinition(201, 7, 500, 4, 5, 6, 1)]);

            var spawn = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadStaticObjectSpawns());

            Assert.Equal((uint)500, spawn.EntryId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_Validate_ReportsMissingOptionalDatasetFileWhenListed()
    {
        string dir = CreatePackageDirectory();
        try
        {
            WritePackage(dir,
                maps: [new MapDefinition(7, "starter")],
                creatures: [new CreatureDefinition(12, "wolf", 3, 80)]);

            var result = DataCatalog.LoadFromDirectory(dir).Validate();

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("Optional dataset 'creatureSpawns'"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreatePackageDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePackage(
        string directory,
        IReadOnlyList<MapDefinition> maps,
        IReadOnlyList<CreatureDefinition> creatures,
        IReadOnlyList<CreatureSpawnDefinition>? creatureSpawns = null,
        IReadOnlyList<StaticObjectSpawnDefinition>? staticObjectSpawns = null)
    {
        DataManifestJson.Save(Sample(), Path.Combine(directory, DataManifest.FileName));
        File.WriteAllText(Path.Combine(directory, "maps.json"), JsonSerializer.Serialize(maps));
        File.WriteAllText(Path.Combine(directory, "spells.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "creatures.json"), JsonSerializer.Serialize(creatures));
        if (creatureSpawns is not null)
        {
            File.WriteAllText(Path.Combine(directory, "creature-spawns.json"), JsonSerializer.Serialize(creatureSpawns));
        }
        if (staticObjectSpawns is not null)
        {
            File.WriteAllText(Path.Combine(directory, "static-object-spawns.json"), JsonSerializer.Serialize(staticObjectSpawns));
        }
    }
}
