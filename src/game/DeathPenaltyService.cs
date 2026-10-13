namespace Ubiquitous.Game;

public static class DeathPenaltyService
{
    private static readonly (int FromLevel, int Copper)[] PenaltyByLevel =
    [
        (11, 500),
        (21, 1500),
        (31, 3000),
        (41, 5000),
        (51, 8000),
    ];

    public static int AmountFor(int level)
    {
        var amount = 0;
        foreach (var (fromLevel, copper) in PenaltyByLevel)
        {
            if (level < fromLevel) break;
            amount = copper;
        }

        return amount;
    }

    public static int GoldLost(int level, int carriedCurrency, bool killedByPlayer) =>
        killedByPlayer || carriedCurrency <= 0 ? 0 : Math.Min(AmountFor(level), carriedCurrency);
}
