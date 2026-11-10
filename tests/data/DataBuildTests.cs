using Ubiquitous.Data;

namespace Ubiquitous.Data.Tests;

public class DataBuildTests
{
    private static readonly DataBuild Build = new("1.1.4", new Dictionary<string, TableShape>
    {
        ["items"] = new("items", 5034, "uuuk"),
        ["zones"] = new("zones", 240, "iiiu"),
    });

    [Fact]
    public void TheSameTablesDoNotDiffer()
    {
        Assert.Empty(Build.Differences([new("items", 5034, "uuuk"), new("zones", 240, "iiiu")]));
    }

    [Fact]
    public void ATableOfAnotherShapeOrLengthOrOneThisBuildLacksDiffers()
    {
        var differing = Build.Differences(
        [
            new("zones", 240, "iiiuu"),
            new("items", 6515, "uuuk"),
            new("mountprototype", 40, "uuss"),
        ]);

        Assert.Equal(["items", "mountprototype", "zones"], differing);
    }

    [Fact]
    public void APackageThatKeptNoShapesComparesNothing()
    {
        Assert.Empty(DataBuild.Unknown.Differences([new("items", 1, "u")]));
    }

    [Fact]
    public void ShapesAreReadFromThePackagesFingerprint()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-build-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, DataBuild.FileName), """
                {
                  "sourceHash": "abc",
                  "tables": [
                    { "name": "tables/items.tbl", "rows": 5034, "cols": 4, "format": "uuuk" },
                    { "name": "tables/broken.tbl", "rows": -1, "cols": -1, "format": "", "error": "unreadable" }
                  ]
                }
                """);

            var build = DataBuild.Load("1.1.4", directory);

            Assert.Equal("1.1.4", build.GameVersion);
            Assert.Equal(new TableShape("items", 5034, "uuuk"), Assert.Single(build.Tables).Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
