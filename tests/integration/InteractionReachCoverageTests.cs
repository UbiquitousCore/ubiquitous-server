using System.Text.RegularExpressions;

namespace Ubiquitous.Integration.Tests;

public class InteractionReachCoverageTests
{
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

    private static string ReadServerSource(string fileName)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "src", "server", fileName));
    }

    [Fact]
    public void EveryRoutedMessageDeclaresWhatItReachesFor()
    {
        var routed = Regex.Matches(ReadServerSource("PlaySession.cs"), @"WebMessageTypes\.(\w+)\s*=>")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(routed);

        var registered = Regex.Matches(ReadServerSource("PlaySession.Reach.cs"), @"\[WebMessageTypes\.(\w+)\]")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = routed.Except(registered, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            $"These routed messages name no interaction reach — add a row to ReachByMessageType "
            + $"(InteractionBody.None is fine if they reach for nothing): {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryDeclaredReachBelongsToARoutedMessage()
    {
        var routed = Regex.Matches(ReadServerSource("PlaySession.cs"), @"WebMessageTypes\.(\w+)\s*=>")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var registered = Regex.Matches(ReadServerSource("PlaySession.Reach.cs"), @"\[WebMessageTypes\.(\w+)\]")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var stale = registered.Except(routed, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, $"These reach rows name messages nothing routes: {string.Join(", ", stale)}");
    }
}
