namespace Ubiquitous.Data.Tests;

public class NavigationPackageTests
{
    private static NavigationPackageFile RoundTrip(NavigationPackageFile file)
    {
        using var stream = new MemoryStream();
        NavigationPackage.Write(stream, file);
        stream.Position = 0;
        return NavigationPackage.Read(stream);
    }

    [Fact]
    public void WriteThenRead_GivesBackTheSameMesh()
    {
        var mesh = new byte[4096];
        Random.Shared.NextBytes(mesh);

        var read = RoundTrip(new NavigationPackageFile(13, NavigationBakeParameters(), mesh));

        Assert.Equal(13, read.MapId);
        Assert.Equal(mesh, read.Mesh);
    }

    [Fact]
    public void WriteThenRead_KeepsTheRulesTheMapWasBakedWith()
    {
        var read = RoundTrip(new NavigationPackageFile(1, NavigationBakeParameters(), [1, 2, 3]));

        Assert.Equal(0.5f, read.Parameters.CellSize, precision: 4);
        Assert.Equal(0.45f, read.Parameters.BodyRadius, precision: 4);
        Assert.Equal(0.6f, read.Parameters.StepHeight, precision: 4);
        Assert.Equal(48f, read.Parameters.MaxSlopeDegrees, precision: 4);
        Assert.Equal(1.9f, read.Parameters.Headroom, precision: 4);
    }

    [Fact]
    public void WriteThenRead_HandlesAMapWithNoGroundAtAll()
    {
        var read = RoundTrip(new NavigationPackageFile(2, NavigationBakeParameters(), []));

        Assert.Empty(read.Mesh);
    }

    [Fact]
    public void WriteThenRead_KeepsTheWaterBesideTheMesh()
    {
        var mesh = new byte[512];
        Random.Shared.NextBytes(mesh);

        var read = RoundTrip(new NavigationPackageFile(7, NavigationBakeParameters(), mesh,
            [new NavigationWater(16f, 8f, 8f, 2.5f), new NavigationWater(24f, 8f, 8f, 2.5f)]));

        Assert.Equal(mesh, read.Mesh);
        Assert.Equal([new NavigationWater(16f, 8f, 8f, 2.5f), new NavigationWater(24f, 8f, 8f, 2.5f)], read.Water);
    }

    [Fact]
    public void Read_TakesAFileFromBeforeWaterAsAMapWithNone()
    {
        using var stream = new MemoryStream();
        NavigationPackage.Write(stream, new NavigationPackageFile(1, NavigationBakeParameters(), [1, 2, 3]));
        var bytes = stream.ToArray().ToList();
        // Version 2 had no water count between the mesh length and the mesh.
        bytes[7] = 2;
        bytes.RemoveRange(7 + 2 + 2 + 20 + 4, 4);

        var read = NavigationPackage.Read(new MemoryStream([.. bytes]));

        Assert.Equal([1, 2, 3], read.Mesh);
        Assert.Empty(read.Water);
    }

    [Fact]
    public void Read_RefusesSomethingThatIsNotAWalkableGroundFile()
    {
        using var stream = new MemoryStream("not walkable ground at all"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => NavigationPackage.Read(stream));
    }

    [Fact]
    public void Read_RefusesAFileFromABuildThatWroteADifferentFormat()
    {
        using var stream = new MemoryStream();
        NavigationPackage.Write(stream, new NavigationPackageFile(1, NavigationBakeParameters(), [1, 2, 3]));
        var bytes = stream.ToArray();

        bytes[7] = NavigationPackage.FormatVersion + 1;

        var error = Assert.Throws<InvalidDataException>(() => NavigationPackage.Read(new MemoryStream(bytes)));
        Assert.Contains("Re-bake", error.Message, StringComparison.Ordinal);
    }

    private static NavigationParameters NavigationBakeParameters() => new(0.5f, 0.45f, 0.6f, 48f, 1.9f);
}

public class NavigationOverrideFileTests
{
    [Fact]
    public void Parse_ReadsEveryKindOfCorrection()
    {
        var overrides = NavigationOverrideFile.Parse(
            """
            {
              "mapId": 13,
              "edits": [
                { "kind": "unwalkable", "minX": 1, "maxX": 4, "minZ": 2, "maxZ": 5, "note": "roof ledge" },
                { "kind": "walkable", "minX": 6, "maxX": 8, "minZ": 0, "maxZ": 2, "floorY": 12.5 }
              ]
            }
            """);

        Assert.Equal(13, overrides.MapId);
        Assert.Equal(
            [NavigationEditKind.Unwalkable, NavigationEditKind.Walkable],
            overrides.Edits.Select(edit => edit.Kind));
        Assert.Equal("roof ledge", overrides.Edits[0].Note);
        Assert.Equal(12.5f, overrides.Edits[1].FloorY!.Value, precision: 4);
    }

    [Fact]
    public void Parse_RefusesAnAddedFloorWithNoHeight()
    {
        var error = Assert.Throws<InvalidDataException>(() => NavigationOverrideFile.Parse(
            """{ "mapId": 1, "edits": [ { "kind": "walkable", "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 } ] }"""));

        Assert.Contains("floorY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RefusesACorrectionNobodyImplemented()
    {
        Assert.Throws<InvalidDataException>(() => NavigationOverrideFile.Parse(
            """{ "mapId": 1, "edits": [ { "kind": "paintItBlue" } ] }"""));
    }

    [Fact]
    public void Parse_RefusesABoxCoveringNoGround()
    {
        Assert.Throws<InvalidDataException>(() => NavigationOverrideFile.Parse(
            """{ "mapId": 1, "edits": [ { "kind": "unwalkable", "minX": 4, "maxX": 4, "minZ": 0, "maxZ": 1 } ] }"""));
    }
}
