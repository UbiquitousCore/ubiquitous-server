using Ubiquitous.Data;

namespace Ubiquitous.Data.Tests;

public sealed class PatrolRouteFilesTests
{
    private const string TwoStopRoute = """
        {
          "mapId": 13,
          "routes": [
            { "id": 7, "stops": [ { "x": 1, "y": 2, "z": 3, "pauseSeconds": 4 }, { "x": 5, "y": 6, "z": 7, "pauseSeconds": 0 } ] }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsStopsAsAClosedLoopWithPausesInMilliseconds()
    {
        var route = Assert.Single(PatrolRouteFiles.Parse(TwoStopRoute, "test.json"));

        Assert.Equal(7u, route.Id);
        Assert.Equal((ushort)13, route.MapId);
        Assert.Equal(0, route.LoopToStopIndex);
        Assert.Equal(new WaypointRouteStop(1, 2, 3, 4000), route.Stops[0]);
        Assert.Equal(new WaypointRouteStop(5, 6, 7, 0), route.Stops[1]);
    }

    [Fact]
    public void AFractionalPause_SurvivesParsingAndFormatting()
    {
        var json = TwoStopRoute.Replace("\"pauseSeconds\": 4", "\"pauseSeconds\": 6.5");

        var routes = PatrolRouteFiles.Parse(json, "test.json");

        Assert.Equal(6500, routes[0].Stops[0].PauseMilliseconds);
        Assert.Contains("\"pauseSeconds\": 6.5", PatrolRouteFiles.Format(13, routes));
    }

    [Theory]
    [InlineData("""{ "mapId": 1, "routes": [ { "id": 1, "stops": [ { "x": 0, "y": 0, "z": 0, "pauseSeconds": 0 } ] } ] }""")]
    [InlineData("""{ "mapId": 1, "routes": [ { "id": 1, "stops": [ { "x": 0, "y": 0, "z": 0, "pauseSeconds": -1 }, { "x": 1, "y": 0, "z": 0, "pauseSeconds": 0 } ] } ] }""")]
    [InlineData("""{ "mapId": 1 }""")]
    public void Parse_RefusesARouteNobodyCouldWalk(string json)
    {
        Assert.Throws<InvalidDataException>(() => PatrolRouteFiles.Parse(json, "bad.json"));
    }

    [Fact]
    public void LoadDirectory_RefusesTheSameRouteIdInTwoFiles()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-routes-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "a.json"), TwoStopRoute);
            File.WriteAllText(Path.Combine(directory.FullName, "b.json"), TwoStopRoute);

            Assert.Throws<InvalidDataException>(() => PatrolRouteFiles.LoadDirectory(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadDirectory_WithNoDirectory_HasNoRoutes()
    {
        Assert.Empty(PatrolRouteFiles.LoadDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ShippedRoutes_AllLoad()
    {
        var routes = PatrolRouteFiles.LoadDirectory(
            Path.Combine(RepositoryRoot(), "content", PatrolRouteFiles.DirectoryName));

        Assert.All(routes, pair => Assert.Equal(pair.Key, pair.Value.Id));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ubiquitous-server.sln"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find ubiquitous-server.sln above the test output directory.");
    }
}
