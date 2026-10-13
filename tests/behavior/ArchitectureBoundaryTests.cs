using System.Xml.Linq;

namespace Ubiquitous.Behavior.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] ForbiddenGameAndWorldReferences =
    [
        "web",
        "persistence",
    ];

    [Fact]
    public void GameAndWorldProjects_ReferenceOnlyLowerLayers()
    {
        var root = FindRepositoryRoot();
        var gameReferences = ReadProjectReferences(root, "src/game/game.csproj");
        var worldReferences = ReadProjectReferences(root, "src/world/world.csproj");

        Assert.Equal(["world"], gameReferences);
        Assert.Equal(["data"], worldReferences);

        foreach (var forbidden in ForbiddenGameAndWorldReferences)
        {
            Assert.DoesNotContain(forbidden, gameReferences);
            Assert.DoesNotContain(forbidden, worldReferences);
        }
    }

    [Fact]
    public void ScriptsProject_SitsBetweenGameAndTheCompositionRoot()
    {
        var root = FindRepositoryRoot();

        Assert.Equal(["game"], ReadProjectReferences(root, "src/scripts/scripts.csproj"));
        Assert.DoesNotContain("scripts", ReadProjectReferences(root, "src/game/game.csproj"));
        Assert.DoesNotContain("scripts", ReadProjectReferences(root, "src/world/world.csproj"));
        Assert.Contains("scripts", ReadProjectReferences(root, "src/server/server.csproj"));
    }

    [Fact]
    public void CompositionRoot_IsTheOnlyProjectReferencingTransportAdapters()
    {
        var root = FindRepositoryRoot();
        var sourceProjects = Directory.EnumerateFiles(
            Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);

        foreach (var projectPath in sourceProjects)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var references = ReadProjectReferences(root, Path.GetRelativePath(root, projectPath));

            if (projectName == "server")
            {
                Assert.Contains("web", references);
                continue;
            }

            Assert.DoesNotContain("web", references);
        }
    }

    [Fact]
    public void ModuleApi_ReferencesOnlyGame_AndOnlyTheServerReferencesModules()
    {
        var root = FindRepositoryRoot();
        Assert.Equal(["game"], ReadProjectReferences(root, "src/module-api/module-api.csproj"));

        var modules = Directory.EnumerateFiles(Path.Combine(root, "modules"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet();
        foreach (var projectPath in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (Path.GetFileNameWithoutExtension(projectPath) == "server") continue;
            var references = ReadProjectReferences(root, Path.GetRelativePath(root, projectPath));
            Assert.DoesNotContain(references, modules.Contains);
        }
    }

    [Fact]
    public void Modules_ReachTheServerOnlyThroughTheModuleApi()
    {
        var root = FindRepositoryRoot();
        foreach (var projectPath in Directory.EnumerateFiles(Path.Combine(root, "modules"), "*.csproj", SearchOption.AllDirectories))
        {
            var references = ReadProjectReferences(root, Path.GetRelativePath(root, projectPath));
            Assert.Contains("module-api", references);
            Assert.DoesNotContain("server", references);
            Assert.DoesNotContain("persistence", references);
        }
    }

    private static IReadOnlyList<string> ReadProjectReferences(string root, string relativePath)
    {
        var projectPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                ((string?)reference.Attribute("Include") ?? "").Replace('\\', '/')))
            .Where(name => name.Length > 0)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ubiquitous-server.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
