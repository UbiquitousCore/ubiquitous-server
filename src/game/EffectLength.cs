namespace Ubiquitous.Game;

// A spell that gives no duration lasts until something ends it: stealth until the hero attacks or is
// hurt, a shield until it is used up, a buff until it is cleansed, dispelled or the unit dies.
// TODO: Verify against play that no such spell ends on a timer of its own.
public static class EffectLength
{
    public static DateTimeOffset ExpiresAt(DateTimeOffset now, int durationMs) =>
        durationMs > 0 ? now + TimeSpan.FromMilliseconds(durationMs) : DateTimeOffset.MaxValue;

    public static TimeSpan? Lasts(int durationMs) => durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : null;

    // What `aura.applied` says it lasts; 0 is a client's sign for one with no end of its own.
    public static int ShownMs(int durationMs) => Math.Max(0, durationMs);
}
