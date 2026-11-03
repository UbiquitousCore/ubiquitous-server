namespace Ubiquitous.Game;

// Legend copies wear a player down: a point for every 45 s inside, more for
// each boss killed, never past 100, and none left after the daily reset.
public static class Exhaustion
{
    public const int Max = 100;

    public static readonly TimeSpan RisesEvery = TimeSpan.FromSeconds(45);

    // Own choice: the daily reset falls at midnight UTC.
    public static int DayOf(DateTimeOffset at) => (int)(at.UtcTicks / TimeSpan.TicksPerDay);

    public static int Today(int amount, int? raisedOn, DateTimeOffset now) =>
        raisedOn == DayOf(now) ? amount : 0;

    public static int Raise(int amount, int by) => Math.Clamp(amount + by, 0, Max);
}
