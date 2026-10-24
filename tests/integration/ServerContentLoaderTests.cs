using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Data;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ServerContentLoaderTests
{
    [Fact]
    public void WithNoDataPackage_ServesDevelopmentSpawns()
    {
        var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

        Assert.Equal(DevelopmentFixtures.CreatureSpawns, content.CreatureSpawns);
        Assert.Equal(DevelopmentFixtures.StaticObjectSpawns, content.StaticObjectSpawns);
    }

    [Fact]
    public void APlacedPatrolRouteFile_IsServed()
    {
        var root = Directory.CreateTempSubdirectory("ubiquitous-content-");
        try
        {
            var routes = Directory.CreateDirectory(Path.Combine(root.FullName, "patrol-routes"));
            File.WriteAllText(Path.Combine(routes.FullName, "test.json"), """
                {"mapId": 13, "routes": [{"id": 4541, "stops": [
                  {"x": 0, "y": 0, "z": 0, "pauseSeconds": 6},
                  {"x": 10, "y": 0, "z": 0, "pauseSeconds": 0}]}]}
                """);
            var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, root.FullName).Load();

            var route = Assert.Single(content.Game.WaypointRoutes).Value;
            Assert.Equal(4541u, route.Id);
            Assert.Equal(6000, route.Stops[0].PauseMilliseconds);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlacedPatrolRoutes_ComeFromTheContentDirectoryTheLoaderIsGiven()
    {
        var empty = Directory.CreateTempSubdirectory("ubiquitous-content-");
        try
        {
            var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, empty.FullName).Load();

            Assert.Empty(content.Game.WaypointRoutes);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADatasetTheManifestListsButCannotBeRead_StopsTheServerLoadingContent()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            File.WriteAllText(Path.Combine(package.FullName, "maps.json"), "{ not json");
            var catalog = new DataCatalog(Manifest(new() { ["maps"] = "maps.json" }), package.FullName);

            var loader = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find());

            var error = Assert.Throws<InvalidDataException>(loader.Load);
            Assert.Contains("maps", error.Message);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADatasetTheManifestDoesNotList_StillFallsBackToDevelopmentContent()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var catalog = new DataCatalog(Manifest(new()), package.FullName);

            var content = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

            Assert.Equal(DevelopmentFixtures.CreatureSpawns, content.CreatureSpawns);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARealPackage_StartsNewCharactersWithNoMoney()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var catalog = new DataCatalog(Manifest(new()), package.FullName);

            var content = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

            Assert.Equal(0, content.Game.StartingCurrency);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    private static DataManifest Manifest(Dictionary<string, string> datasets) =>
        new("test", DataManifest.TargetGameVersion, "hash", DateTimeOffset.UnixEpoch, datasets);

    [Fact]
    public void TheContentDirectory_IsTheRepositorysOwnWhenRunFromIt()
    {
        var found = ContentDirectory.Find();

        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(found)!, "ubiquitous-server.sln")));
        Assert.Equal("content", Path.GetFileName(found));
    }

    [Fact]
    public void TheContentDirectory_IsTheOneBesideTheServerOutsideARepository()
    {
        var outside = Directory.CreateTempSubdirectory("ubiquitous-published-");
        try
        {
            Assert.Equal(Path.Combine(outside.FullName, "content"), ContentDirectory.Find(outside.FullName));
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }
}
