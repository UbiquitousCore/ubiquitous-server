namespace Ubiquitous.Game;

public static class CoinWords
{
    private const int Silver = 100;
    public const int Gold = 100 * Silver;

    public static string Of(long copper)
    {
        var total = Math.Max(0, copper);
        var parts = new List<string>(3);
        if (total / Gold > 0) parts.Add($"{total / Gold} gold");
        if (total % Gold / Silver > 0) parts.Add($"{total % Gold / Silver} silver");
        if (total % Silver > 0) parts.Add($"{total % Silver} copper");
        return parts.Count > 0 ? string.Join(' ', parts) : "0 copper";
    }
}
