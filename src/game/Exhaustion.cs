namespace Ubiquitous.Game;

// Legend copies wear a player down: each boss killed adds its amount, never
// past the limit (100 unless the operator raises it), and none is left after
// the daily reset. At the limit a player cannot enter a Legend copy until then.
public static class Exhaustion
{
    public const int Max = 100;

    public const int LegendLevel = 60;

    // Own choice: the daily reset falls at midnight UTC.
    public static int DayOf(DateTimeOffset at) => (int)(at.UtcTicks / TimeSpan.TicksPerDay);

    public static int Today(int amount, int? raisedOn, DateTimeOffset now) =>
        raisedOn == DayOf(now) ? amount : 0;

    public static int Raise(int amount, int by, int limit = Max) => Math.Clamp(amount + by, 0, limit);

    public static LegendRefusal? WhyNotLegend(int level, int exhaustion, int limit = Max) =>
        level < LegendLevel ? LegendRefusal.TooLow
        : exhaustion >= limit ? LegendRefusal.Exhausted
        : null;
}

public enum LegendRefusal
{
    TooLow,
    Exhausted,
}
