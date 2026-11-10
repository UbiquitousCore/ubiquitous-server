using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public sealed class IngestCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ubiquitous-ingest-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void AnEmptyDropFolderAsksForTheIpaAndSaysHowToRunAgain()
    {
        var incoming = _folder.CreateSubdirectory("incoming").FullName;
        var log = new StringWriter();

        var code = IngestCommand.Run(incoming, Path.Combine(_folder.FullName, "data"), log);

        Assert.Equal(2, code);
        Assert.Contains("copy your 1.1.4 .ipa into", log.ToString());
        Assert.Contains("did not finish", log.ToString());
        Assert.False(Directory.Exists(Path.Combine(_folder.FullName, "data", "generated")));
    }

    [Fact]
    public void AMissingDropFolderAsksForTheIpaToo()
    {
        var log = new StringWriter();

        var code = IngestCommand.Run(Path.Combine(_folder.FullName, "incoming"), Path.Combine(_folder.FullName, "data"), log);

        Assert.Equal(2, code);
        Assert.Contains("copy your 1.1.4 .ipa into", log.ToString());
    }

    [Fact]
    public void DoneNamesTheVersionAndTheDockerCommandsToTypeNext()
    {
        var lines = IngestReport.Done(Manifest("1.1.4"), inContainer: true);

        Assert.Equal("Done! The game data is imported.", lines[0]);
        Assert.Contains(lines, line => line.Contains("Game version: 1.1.4, 3 data sets"));
        Assert.Contains("  docker compose up -d", lines);
        Assert.Contains(lines, line => line.Contains("account create <name>"));
        Assert.DoesNotContain(lines, line => line.Contains("Warning"));
    }

    [Fact]
    public void ANativeRunIsToldTheDotnetCommandsInstead()
    {
        var lines = IngestReport.Done(Manifest("1.1.4"), inContainer: false);

        Assert.Contains("  dotnet run --project src/server", lines);
        Assert.DoesNotContain(lines, line => line.Contains("docker"));
    }

    [Fact]
    public void WhatAVersionsFilesLeaveOutIsWarnedAbout()
    {
        var manifest = Manifest("2.2.0") with { ImportNotes = ["tables/loot.tbl: not in this build; creatures drop nothing."] };

        var lines = IngestReport.Done(manifest, inContainer: true);

        Assert.Contains(lines, line => line.Contains("Warning"));
        Assert.Contains(lines, line => line.Contains("creatures drop nothing"));
    }

    private static DataManifest Manifest(string version) => new(
        "test", version, "hash", DateTimeOffset.UnixEpoch,
        new Dictionary<string, string> { ["maps"] = "a", ["spells"] = "b", ["creatures"] = "c" });
}
