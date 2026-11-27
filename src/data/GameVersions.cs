namespace Ubiquitous.Data;

// Which builds a character may be played on. A character keeps the newest build it was
// played on, and an older build's tables may lack its items, levels or spells, so only
// that build or a later one plays it. Own choice.
public static class GameVersions
{
    public static bool IsKnown(string? gameVersion) =>
        !string.IsNullOrEmpty(gameVersion) && gameVersion != DataManifest.UnknownGameVersion;

    // An unknown package plays as 1.1.4, so it holds characters as 1.1.4 would.
    public static string PlaysAs(string? gameVersion) =>
        IsKnown(gameVersion) ? gameVersion! : DataManifest.TargetGameVersion;

    public static int Compare(string a, string b)
    {
        var left = Parts(a);
        var right = Parts(b);
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var order = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (order != 0) return order;
        }

        return 0;
    }

    // A character no build has stamped yet was made before builds were kept, and plays anywhere.
    public static bool MayPlay(string? characterVersion, string? serverVersion) =>
        characterVersion is null || Compare(characterVersion, PlaysAs(serverVersion)) <= 0;

    // What a character saved on this server keeps: the later of its own build and this one.
    // A server that does not know its build stamps nothing.
    public static string? Stamp(string? characterVersion, string? serverVersion) =>
        !IsKnown(serverVersion) ? characterVersion
        : characterVersion is null || Compare(serverVersion!, characterVersion) > 0 ? serverVersion
        : characterVersion;

    private static int[] Parts(string version) =>
        [.. version.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0)];
}
