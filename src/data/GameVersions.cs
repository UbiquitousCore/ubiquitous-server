namespace Ubiquitous.Data;

// Which builds a character may be played on: only the one it was made on. Builds number
// and shape their items, quests, spells and talents differently, and until a migration
// between two builds exists a character played on another would lose or misread them.
// Own choice.
public static class GameVersions
{
    public static bool IsKnown(string? gameVersion) =>
        !string.IsNullOrEmpty(gameVersion) && gameVersion != DataManifest.UnknownGameVersion;

    // An unknown package plays as 1.1.4, so it holds characters as 1.1.4 would.
    public static string PlaysAs(string? gameVersion) =>
        IsKnown(gameVersion) ? gameVersion! : DataManifest.TargetGameVersion;

    // A character no build has stamped was made before builds were kept, and plays anywhere.
    public static bool MayPlay(string? characterVersion, string? serverVersion) =>
        characterVersion is null || characterVersion == PlaysAs(serverVersion);

    // A character keeps the build it was made on; one made before builds were kept takes the
    // first known build that saves it. A server that does not know its build stamps nothing.
    public static string? Stamp(string? characterVersion, string? serverVersion) =>
        characterVersion ?? (IsKnown(serverVersion) ? serverVersion : null);
}
