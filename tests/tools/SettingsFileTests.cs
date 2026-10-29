using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ubiquitous-settings-");

    public void Dispose() => _folder.Delete(recursive: true);

    private string EnvPath => Path.Combine(_folder.FullName, SettingsFile.FileName);

    private static IEnumerable<string> Lines(string text) => text.Split('\n');

    [Fact]
    public void AnAnswerLandsOnTheCommentedLineThatShowsIt()
    {
        var filled = SettingsFile.Fill(
            "# What it does.\n# UBIQUITOUS_SITE=192.168.1.20\nOTHER=1",
            [("UBIQUITOUS_SITE", "game.example.org"), ("UBIQUITOUS_NEW", "x")]);

        Assert.Equal(["# What it does.", "UBIQUITOUS_SITE=game.example.org", "OTHER=1", "UBIQUITOUS_NEW=x"], Lines(filled));
    }

    [Fact]
    public void AnAnswerReplacesALineThatAlreadySetsIt()
    {
        Assert.Equal(["A=new"], Lines(SettingsFile.Fill("A=old", [("A", "new")])));
    }

    [Fact]
    public void EnterAtEveryQuestionChangesNothing()
    {
        var answers = SettingsFile.Ask(new StringReader("\n\n\n"), TextWriter.Null);

        Assert.Empty(answers);
    }

    [Fact]
    public void TheAnswersBecomeSettings()
    {
        var answers = SettingsFile.Ask(new StringReader("192.168.1.20\ny\nyes\n12\n"), TextWriter.Null);

        Assert.Equal(
            [
                ("UBIQUITOUS_SITE", "192.168.1.20"),
                ("UBIQUITOUS_FESTIVAL_CONTENT_ENABLED", "true"),
                ("UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED", "true"),
                ("UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT", "12"),
            ],
            answers);
    }

    [Fact]
    public void WithNobodyToAskTheExampleIsWrittenAsItStands()
    {
        Assert.True(SettingsFile.EnsureExists(_folder.FullName, TextReader.Null, TextWriter.Null, interactive: false));

        var written = File.ReadAllText(EnvPath);
        Assert.Contains("# UBIQUITOUS_FESTIVAL_CONTENT_ENABLED=false", written);
        Assert.Contains("# UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED=false", written);
    }

    [Fact]
    public void AnExistingFileIsNeverTouched()
    {
        File.WriteAllText(EnvPath, "MINE=1");

        Assert.False(SettingsFile.EnsureExists(_folder.FullName, new StringReader("y\ny\n"), TextWriter.Null, interactive: true));
        Assert.Equal("MINE=1", File.ReadAllText(EnvPath));
    }
}
