namespace Ubiquitous.Game;

public readonly record struct PartyEarner(string Id, int Level);

public readonly record struct MoneySplit(int EachShare, int PickerExtra);

public static class PartyRewards
{
    public const float ShareRange = 50f;

    private static readonly int[] TotalPercentBySize = [100, 160, 180, 200];

    // The kill is valued at the highest nearby level, scaled up with the
    // number of earners, and each takes a share in proportion to their own level.
    public static IReadOnlyDictionary<string, int> KillXpShares(
        IReadOnlyList<PartyEarner> earners, int creatureLevel, bool elite)
    {
        if (earners.Count == 0) return new Dictionary<string, int>();
        var topLevel = earners.Max(earner => earner.Level);
        var size = Math.Min(earners.Count, TotalPercentBySize.Length);
        var total = (long)LevelingService.KillXp(topLevel, creatureLevel, elite) * TotalPercentBySize[size - 1] / 100;
        long levelSum = earners.Sum(earner => Math.Max(1, earner.Level));
        return earners.ToDictionary(
            earner => earner.Id,
            earner => (int)(total * Math.Max(1, earner.Level) / levelSum),
            StringComparer.Ordinal);
    }

    public static MoneySplit SplitMoney(int amount, int earners)
    {
        if (amount <= 0) return new MoneySplit(0, 0);
        var count = Math.Max(1, earners);
        return new MoneySplit(amount / count, amount % count);
    }
}
