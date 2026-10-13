namespace Ubiquitous.Game;

public sealed record ActiveAbsorbEffect(string TargetId, uint SpellId, int Remaining, int Percent, DateTimeOffset ExpiresAt);

public sealed class AbsorbEffectService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, ActiveAbsorbEffect> _active = new(StringComparer.Ordinal);
    private readonly List<ActiveAbsorbEffect> _usedUp = new();

    public void Apply(string targetId, uint spellId, int amount, int percent, int durationMs, DateTimeOffset now)
    {
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        _active[targetId] = new ActiveAbsorbEffect(
            targetId, spellId, Math.Max(0, amount), Math.Clamp(percent, 0, 100), now + duration);
    }

    public int Absorb(string targetId, int damage, DateTimeOffset now)
    {
        if (damage <= 0 || !_active.TryGetValue(targetId, out var shield) || now >= shield.ExpiresAt) return damage;
        var share = (int)Math.Round(damage * shield.Percent / 100.0, MidpointRounding.AwayFromZero);
        var taken = Math.Min(share, shield.Remaining);
        var left = shield.Remaining - taken;
        if (left <= 0)
        {
            _active.Remove(targetId);
            _usedUp.Add(shield with { Remaining = 0 });
        }
        else _active[targetId] = shield with { Remaining = left };
        return damage - taken;
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        if (!_active.Remove(targetId, out var shield)) return [];
        return [shield.SpellId];
    }

    public IReadOnlyList<ActiveAbsorbEffect> TickExpired(DateTimeOffset now)
    {
        var ended = new List<ActiveAbsorbEffect>(_usedUp);
        _usedUp.Clear();
        foreach (var shield in _active.Values.Where(shield => now >= shield.ExpiresAt).ToArray())
        {
            _active.Remove(shield.TargetId);
            ended.Add(shield);
        }
        return ended;
    }
}
