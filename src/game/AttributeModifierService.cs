namespace Ubiquitous.Game;

public sealed record ActiveAttributeModifier(
    string TargetId, uint SpellId, int AttributeId, int Amount, bool IsPercent, DateTimeOffset ExpiresAt, string? CasterId = null);

public sealed class AttributeModifierService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private readonly List<ActiveAttributeModifier> _active = new();

    public void Apply(string targetId, uint spellId, int attributeId, int amount, bool isPercent, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        _active.RemoveAll(m => string.Equals(m.TargetId, targetId, StringComparison.Ordinal));
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        _active.Add(new ActiveAttributeModifier(targetId, spellId, attributeId, amount, isPercent, now + duration, casterId));
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        var removed = _active.Where(m => string.Equals(m.TargetId, targetId, StringComparison.Ordinal))
            .Select(m => m.SpellId).ToArray();
        _active.RemoveAll(m => string.Equals(m.TargetId, targetId, StringComparison.Ordinal));
        return removed;
    }

    public IReadOnlyList<ActiveAttributeModifier> ClearWhere(Predicate<ActiveAttributeModifier> match)
    {
        var removed = _active.FindAll(match);
        if (removed.Count > 0) _active.RemoveAll(match);
        return removed;
    }

    private readonly Dictionary<string, IReadOnlyDictionary<int, int>> _worn =
        new(StringComparer.Ordinal);

    public void SetWorn(string targetId, IReadOnlyDictionary<int, int> totals)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(totals);
        if (totals.Count == 0) _worn.Remove(targetId);
        else _worn[targetId] = totals;
    }

    private readonly Dictionary<string, IReadOnlyDictionary<int, int>> _passive =
        new(StringComparer.Ordinal);

    public void SetPassive(string targetId, IReadOnlyDictionary<int, int> totals)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(totals);
        if (totals.Count == 0) _passive.Remove(targetId);
        else _passive[targetId] = totals;
    }

    public int WornAmount(string targetId, int attributeId) =>
        _worn.TryGetValue(targetId, out var totals) && totals.TryGetValue(attributeId, out var amount)
            ? amount
            : 0;

    public int GetFlatAmount(string targetId, int attributeId, DateTimeOffset now)
    {
        var active = _active.Find(m =>
            string.Equals(m.TargetId, targetId, StringComparison.Ordinal)
            && m.AttributeId == attributeId && !m.IsPercent && now < m.ExpiresAt);
        var passive = _passive.TryGetValue(targetId, out var totals) ? totals.GetValueOrDefault(attributeId) : 0;
        return WornAmount(targetId, attributeId) + passive + (active?.Amount ?? 0);
    }

    public IReadOnlyList<ActiveAttributeModifier> TickExpired(DateTimeOffset now)
    {
        var expired = _active.FindAll(m => now >= m.ExpiresAt);
        if (expired.Count > 0) _active.RemoveAll(m => now >= m.ExpiresAt);
        return expired;
    }
}
