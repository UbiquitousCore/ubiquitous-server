namespace Ubiquitous.Game;

public sealed record HeldAura(
    string UnitId, string CasterId, uint SpellId, int RemainingMs, int MaxDurationMs, bool Harmful);

public sealed class AuraBoard
{
    private sealed record Entry(object Reporter, HeldAura Aura, DateTimeOffset ReportedAt);

    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    public void Applied(object reporter, HeldAura aura, DateTimeOffset now)
    {
        lock (_gate)
        {
            var entry = new Entry(reporter, aura, now);
            var at = _entries.FindIndex(e => ReferenceEquals(e.Reporter, reporter)
                && string.Equals(e.Aura.UnitId, aura.UnitId, StringComparison.Ordinal) && e.Aura.SpellId == aura.SpellId);
            if (at >= 0) _entries[at] = entry;
            else _entries.Add(entry);
        }
    }

    public void Ended(object reporter, string unitId, uint spellId)
    {
        lock (_gate)
            _entries.RemoveAll(e => ReferenceEquals(e.Reporter, reporter)
                && string.Equals(e.Aura.UnitId, unitId, StringComparison.Ordinal)
                && (spellId == 0 || e.Aura.SpellId == spellId));
    }

    public void Died(string unitId)
    {
        lock (_gate)
            _entries.RemoveAll(e => string.Equals(e.Aura.UnitId, unitId, StringComparison.Ordinal));
    }

    public void Forget(object reporter, string unitId)
    {
        lock (_gate)
            _entries.RemoveAll(e => ReferenceEquals(e.Reporter, reporter)
                && string.Equals(e.Aura.UnitId, unitId, StringComparison.Ordinal));
    }

    public void Forget(object reporter)
    {
        lock (_gate) _entries.RemoveAll(e => ReferenceEquals(e.Reporter, reporter));
    }

    public IReadOnlyList<HeldAura> On(string unitId, DateTimeOffset now)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => Left(e, now) is <= 0);
            var worn = new List<HeldAura>();
            foreach (var entry in _entries)
            {
                if (!string.Equals(entry.Aura.UnitId, unitId, StringComparison.Ordinal)) continue;
                var aged = entry.Aura with { RemainingMs = Left(entry, now) ?? 0 };
                var same = worn.FindIndex(one => one.SpellId == aged.SpellId);
                if (same < 0) worn.Add(aged);
                else if (aged.RemainingMs > worn[same].RemainingMs) worn[same] = aged;
            }
            return worn;
        }
    }

    private static int? Left(Entry entry, DateTimeOffset now)
    {
        var from = entry.Aura.RemainingMs > 0 ? entry.Aura.RemainingMs : entry.Aura.MaxDurationMs;
        if (from <= 0) return null;
        var elapsed = (now - entry.ReportedAt).TotalMilliseconds;
        return (int)Math.Max(0, Math.Min(from, Math.Ceiling(from - elapsed)));
    }
}
