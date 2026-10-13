using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class NavigationBakeCommandTests
{
    private static TerrainTile Flat(int tileX, int tileZ, short hundredths)
    {
        var heights = new short[TerrainPackage.SamplesPerTile];
        Array.Fill(heights, hundredths);
        return new TerrainTile(tileX, tileZ, heights);
    }

    private static string WritePackage(params (ushort MapId, TerrainTile[] Tiles)[] maps)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-bake-{Guid.NewGuid():N}");
        var terrain = Path.Combine(directory, TerrainPackage.DirectoryName);
        Directory.CreateDirectory(terrain);
        foreach (var (mapId, tiles) in maps)
        {
            using var stream = File.Create(Path.Combine(terrain, TerrainPackage.FileName(mapId)));
            TerrainPackage.Write(stream, mapId, tiles);
        }

        return directory;
    }

    [Fact]
    public void Bake_SaysHowManyOfAMapsOwnCreaturesItCanStandOnTheGround()
    {
        var package = WritePackage((7, [Flat(0, 0, 250)]));
        try
        {
            File.WriteAllText(Path.Combine(package, "creatureSpawns.json"), """
                [
                  { "id": 1, "mapId": 7, "creatureEntryId": 1, "x": 20, "y": 2.5, "z": 20 },
                  { "id": 2, "mapId": 7, "creatureEntryId": 1, "x": 40, "y": 2.5, "z": 30 },
                  { "id": 3, "mapId": 7, "creatureEntryId": 1, "x": 20, "y": 90,  "z": 20 },
                  { "id": 4, "mapId": 7, "creatureEntryId": 1, "x": 900, "y": 2.5, "z": 900 }
                ]
                """);

            var log = new StringWriter();
            var report = Assert.Single(NavigationBakeCommand.Run(package, null, null, null, null, log));

            Assert.Equal(4, report.Spawns);
            Assert.Equal(2, report.SpawnsOnGround);
            Assert.Contains("2 of 4 creatures stand on it", log.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_SaysNothingAboutCreaturesWhereThePackageCarriesNone()
    {
        var package = WritePackage((7, [Flat(0, 0, 250)]));
        try
        {
            var log = new StringWriter();
            var report = Assert.Single(NavigationBakeCommand.Run(package, null, null, null, null, log));

            Assert.Equal(0, report.Spawns);
            Assert.DoesNotContain("creatures stand on it", log.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_WritesOneFileForEveryMapWithTerrainAndSaysWhatItDid()
    {
        var package = WritePackage(
            (7, [Flat(0, 0, 250)]),
            (9, [Flat(0, 0, -100), Flat(1, 0, -100)]));
        try
        {
            var log = new StringWriter();
            var reports = NavigationBakeCommand.Run(package, null, null, null, null, log);

            Assert.Equal([(ushort)7, (ushort)9], reports.Select(report => report.MapId));
            Assert.All(reports, report => Assert.True(report.Summary.PolygonTotal > 0));
            Assert.Contains("map   7", log.ToString(), StringComparison.Ordinal);
            Assert.Contains("polygons", log.ToString(), StringComparison.Ordinal);

            var source = WalkableGroundSource.LoadFromDirectory(
                Path.Combine(package, NavigationPackage.DirectoryName));
            Assert.Equal(2, source.MapCount);

            AssertGround(2.5f, source.GroundHeightNear(7, 32f, 32f, 2.5f));
            AssertGround(-1f, source.GroundHeightNear(9, 70f, 20f, -1f));
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_AgreesWithTheTerrainTheServerAlreadySamples()
    {
        var heights = new short[TerrainPackage.SamplesPerTile];
        for (int z = 0; z < TerrainPackage.SamplesPerSide; z++)
        {
            for (int x = 0; x < TerrainPackage.SamplesPerSide; x++)
            {
                heights[(z * TerrainPackage.SamplesPerSide) + x] = (short)(x * 10);
            }
        }

        var package = WritePackage((3, [new TerrainTile(0, 0, heights)]));
        try
        {
            NavigationBakeCommand.Run(package, null, null, null, null, new StringWriter());
            var source = WalkableGroundSource.LoadFromDirectory(
                Path.Combine(package, NavigationPackage.DirectoryName));

            var heightmap = new TerrainHeightmap([new TerrainTile(0, 0, heights)]);
            foreach (var (x, z) in new[] { (20f, 20f), (33.5f, 41.25f), (10f, 55f) })
            {
                float sampled = heightmap.HeightAt(x, z)!.Value;
                float baked = source.GroundHeightNear(3, x, z, sampled)!.Value;
                Assert.Equal(sampled, baked, tolerance: 0.2f);
            }
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_LeavesCorrectionsWhereTheyAre()
    {
        var package = WritePackage((7, [Flat(0, 0, 0)]));
        try
        {
            var navigation = Path.Combine(package, NavigationPackage.DirectoryName);
            NavigationBakeCommand.Run(package, null, null, null, null, new StringWriter());

            var overridePath = Path.Combine(navigation, NavigationPackage.OverrideFileName(7));
            var corrections =
                """
                {
                  "mapId": 7,
                  "edits": [
                    { "kind": "unwalkable", "minX": 20, "maxX": 40, "minZ": 20, "maxZ": 40, "note": "kept by hand" }
                  ]
                }
                """;
            File.WriteAllText(overridePath, corrections);

            NavigationBakeCommand.Run(package, null, null, null, null, new StringWriter());

            Assert.Equal(corrections, File.ReadAllText(overridePath));

            var source = WalkableGroundSource.LoadFromDirectory(navigation);
            Assert.Null(source.GroundHeightNear(7, 30f, 30f, 0f));
            AssertGround(0f, source.GroundHeightNear(7, 10f, 10f, 0f));
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_CanBeAskedForOneMap()
    {
        var package = WritePackage((7, [Flat(0, 0, 0)]), (9, [Flat(0, 0, 0)]));
        try
        {
            var reports = NavigationBakeCommand.Run(package, null, null, null, onlyMap: 9, new StringWriter());

            var report = Assert.Single(reports);
            Assert.Equal(9, report.MapId);
            Assert.False(File.Exists(Path.Combine(
                package, NavigationPackage.DirectoryName, NavigationPackage.FileName(7))));
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    [Fact]
    public void Bake_StandsOnWhatAMapIsBuiltFromWhenItsWorldArchiveIsThere()
    {
        var package = WritePackage((13, [Flat(0, 0, 0)]));
        var extract = Path.Combine(Path.GetTempPath(), $"ubiquitous-bake-extract-{Guid.NewGuid():N}");
        try
        {
            var tables = Path.Combine(extract, "tables");
            var models = Path.Combine(extract, "model");
            var world = Path.Combine(extract, "world");
            Directory.CreateDirectory(tables);
            Directory.CreateDirectory(Path.Combine(models, "build"));
            Directory.CreateDirectory(world);

            File.WriteAllBytes(Path.Combine(tables, "map_info.tbl"), MapInfoTable(13, "Town", "world/town.wld"));

            foreach (var empty in new[] { "staticobject_spawns", "staticobject_proto", "staticobject_displayinfo" })
            {
                File.WriteAllBytes(
                    Path.Combine(tables, $"{empty}.tbl"),
                    FTableFixture.Build(rows: 0, cols: 16, entrySize: 64, format: new string('u', 16)));
            }

            File.WriteAllBytes(Path.Combine(models, "build", "hut.phy"), Slab(centreY: 4f, halfY: 0.25f, half: 6f));
            File.WriteAllBytes(
                Path.Combine(world, "town.bin"),
                PackedArchiveFixture.Build(("world/town/items/0000_0000.itm", WorldItemReaderTests.Tile(
                    0, 0, 4, new WorldItemReaderTests.Record("model/build/hut.bdae", (32f, 0f, 32f))))));

            var log = new StringWriter();
            var report = Assert.Single(NavigationBakeCommand.Run(package, tables, models, world, null, log));

            Assert.Equal(1, report.BuildingPlacements);
            Assert.Equal(1, report.BuildingsWithCollision);
            Assert.Contains("placed by the map itself", log.ToString(), StringComparison.Ordinal);

            var source = WalkableGroundSource.LoadFromDirectory(
                Path.Combine(package, NavigationPackage.DirectoryName));

            AssertGround(0f, source.GroundHeightNear(13, 32f, 32f, 0f));
            AssertGround(4.25f, source.GroundHeightNear(13, 32f, 32f, 4.25f));
        }
        finally
        {
            Directory.Delete(package, recursive: true);
            Directory.Delete(extract, recursive: true);
        }
    }

    [Fact]
    public void Bake_WithoutTheArchives_IsExactlyWhatItWasBefore()
    {
        var package = WritePackage((13, [Flat(0, 0, 0)]));
        try
        {
            var log = new StringWriter();
            var report = Assert.Single(NavigationBakeCommand.Run(package, null, null, null, null, log));

            Assert.Equal(0, report.BuildingPlacements);
            Assert.DoesNotContain("placed by the map itself", log.ToString(), StringComparison.Ordinal);
            Assert.Contains("world archives: none given", log.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(package, recursive: true);
        }
    }

    private static byte[] Slab(float centreY, float halfY, float half)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(3);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(centreY);
        writer.Write(half);
        writer.Write(half);
        writer.Write(halfY);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] MapInfoTable(uint mapId, string name, string file)
    {
        const string format = "uuuuuuuuuss";
        const int headerSize = 44;
        const int entrySize = (9 * 4) + 6 + 6;
        int offsetFormat = headerSize + 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + entrySize;

        var pool = new List<byte>();
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        var fileBytes = System.Text.Encoding.UTF8.GetBytes(file + "\0");
        int nameAt = offsetStrings;
        pool.AddRange(nameBytes);
        int fileAt = offsetStrings + pool.Count;
        pool.AddRange(fileBytes);

        var data = new byte[offsetStrings + pool.Count];
        pool.CopyTo(data, offsetStrings);
        System.Text.Encoding.ASCII.GetBytes("FTABLE").CopyTo(data, 0);
        Write(8, 0x100000);
        Write(12, 1);
        Write(16, format.Length);
        Write(20, headerSize);
        Write(24, offsetFormat);
        Write(28, offsetEntry);
        Write(32, entrySize);
        Write(36, offsetStrings);
        Write(40, pool.Count);
        System.Text.Encoding.ASCII.GetBytes(format).CopyTo(data, offsetFormat);
        Write(offsetEntry, (int)mapId);
        Write(offsetEntry + (9 * 4), nameAt);
        data[offsetEntry + (9 * 4) + 4] = (byte)nameBytes.Length;
        Write(offsetEntry + (9 * 4) + 6, fileAt);
        data[offsetEntry + (9 * 4) + 10] = (byte)fileBytes.Length;
        return data;

        void Write(int at, int value)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
            data[at + 2] = (byte)(value >> 16);
            data[at + 3] = (byte)(value >> 24);
        }
    }

    [Fact]
    public void Bake_SaysSoWhenThereIsNoTerrainToBakeFrom()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-bake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => NavigationBakeCommand.Run(directory, null, null, null, null, new StringWriter()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractWorld_TakesTheMapsOutOfAWholeClientBuild()
    {
        var root = Directory.CreateTempSubdirectory("ubiquitous-build-");
        try
        {
            var build = Path.Combine(root.FullName, "build.bin");
            File.WriteAllBytes(build, Packed(
                ("Payload/App/atlas.bin", Packed(("notes.txt", new byte[] { 1, 2, 3 }))),
                ("Payload/App/somewhere.bin", Packed(("somewhere/terrain/0_0.trn", new byte[] { 4, 5, 6 })))));

            var exit = Program.Main(["--extract-world", build, "--out", root.FullName]);

            Assert.Equal(0, exit);
            var world = Path.Combine(root.FullName, "world");
            Assert.True(File.Exists(Path.Combine(world, "somewhere.bin")), "the map should come out");
            Assert.False(File.Exists(Path.Combine(world, "atlas.bin")), "and the thing that is not one should not");
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static byte[] Packed(params (string Name, byte[] Contents)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                using var entry = zip.CreateEntry(
                    name, System.IO.Compression.CompressionLevel.Fastest).Open();
                entry.Write(contents);
            }
        }

        return stream.ToArray();
    }

    private static void AssertGround(float expected, float? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(
            actual!.Value,
            expected - NavigationBaker.CellHeight,
            expected + NavigationBaker.CellHeight);
    }
}
