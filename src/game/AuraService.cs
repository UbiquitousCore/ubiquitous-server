namespace Ubiquitous.Game;

public sealed record ActiveAura(
    string TargetId, uint SpellId, bool IsHeal, int PerTick, int TicksRemaining, DateTimeOffset NextTickAt, string? CasterId = null);

public sealed record AuraTick(string TargetId, uint SpellId, bool IsHeal, int PerTick, bool Expired, string? CasterId = null);

public sealed class AuraService
{
    public const int TickCount = 3;
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
    public static TimeSpan Duration => TickInterval * TickCount;

    private readonly List<ActiveAura> _active = new();

    public void Apply(string targetId, uint spellId, int perTick, bool isHeal, DateTimeOffset now, string? casterId = null)
    {
        _active.RemoveAll(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId);
        _active.Add(new ActiveAura(targetId, spellId, isHeal, perTick, TickCount, now + TickInterval, casterId));
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        var removed = _active.Where(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal))
            .Select(a => a.SpellId).ToArray();
        _active.RemoveAll(a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal));
        return removed;
    }

    public IReadOnlyList<ActiveAura> ClearWhere(Predicate<ActiveAura> match)
    {
        var removed = _active.FindAll(match);
        if (removed.Count > 0) _active.RemoveAll(match);
        return removed;
    }

    public IReadOnlyList<AuraTick> Tick(DateTimeOffset now)
    {
        var ticks = new List<AuraTick>();
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var aura = _active[i];
            if (now < aura.NextTickAt) continue;

            bool expiring = aura.TicksRemaining <= 1;
            ticks.Add(new AuraTick(aura.TargetId, aura.SpellId, aura.IsHeal, aura.PerTick, expiring, aura.CasterId));
            if (expiring)
            {
                _active.RemoveAt(i);
            }
            else
            {
                _active[i] = aura with { TicksRemaining = aura.TicksRemaining - 1, NextTickAt = now + TickInterval };
            }
        }

        return ticks;
    }
}
