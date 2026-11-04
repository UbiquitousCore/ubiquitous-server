namespace Ubiquitous.Game;

// What an operator can turn up or down on their own server. Normal is the
// game as it plays; every other value is the operator's choice.
public sealed record GameRates(
    double Xp = 1.0,
    int ExhaustionLimit = Exhaustion.Max,
    double PlayerDamage = 1.0,
    double CreatureDamage = 1.0,
    double CreatureHealth = 1.0)
{
    public static GameRates Normal { get; } = new();

    public int ScaleXp(int gained) => Scale(gained, Xp);

    public int ScaleDamage(int damage, bool dealtByPlayer) =>
        Scale(damage, dealtByPlayer ? PlayerDamage : CreatureDamage);

    // A creature always keeps at least one point, so a tiny rate cannot leave
    // something alive that has no health to lose.
    public int ScaleCreatureHealth(int maxHealth) =>
        maxHealth <= 0 ? maxHealth : Math.Max(1, Scale(maxHealth, CreatureHealth));

    private static int Scale(int amount, double rate) =>
        amount <= 0 || rate == 1.0
            ? amount
            : (int)Math.Min(int.MaxValue, Math.Round(amount * rate, MidpointRounding.AwayFromZero));
}
