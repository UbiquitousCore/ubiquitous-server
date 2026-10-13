namespace Ubiquitous.Server;

internal static class PlayerWireIds
{
    private const string Prefix = "player:";

    public static string For(string characterGuid) => Prefix + characterGuid;

    public static string? GuidOf(string? wireId)
    {
        if (string.IsNullOrWhiteSpace(wireId) || !wireId.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var guid = wireId[Prefix.Length..];
        return guid.Length == 0 ? null : guid;
    }
}
