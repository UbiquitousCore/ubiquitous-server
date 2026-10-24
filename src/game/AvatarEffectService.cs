namespace Ubiquitous.Game;

public sealed record ActiveAvatar(string TargetId, uint SpellId, DateTimeOffset? ExpiresAt);

// The shape a body has been turned into by an aura (a cookie, a wand, a spirit). One at a time per body;
// a spell with no duration lasts until it is cast again or the body is cleared.
public sealed class AvatarEffectService
{
    private readonly Dictionary<string, ActiveAvatar> _active = new(StringComparer.Ordinal);

    public uint? ActiveOn(string targetId) => _active.TryGetValue(targetId, out var held) ? held.SpellId : null;

    // The spell it replaced, if another shape was worn.
    public uint? Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now)
    {
        uint? replaced = _active.TryGetValue(targetId, out var held) && held.SpellId != spellId ? held.SpellId : null;
        _active[targetId] = new ActiveAvatar(
            targetId, spellId, durationMs > 0 ? now + TimeSpan.FromMilliseconds(durationMs) : null);
        return replaced;
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        _active.Remove(targetId, out var held) ? [held.SpellId] : [];

    public IReadOnlyList<ActiveAvatar> TickExpired(DateTimeOffset now)
    {
        var ended = _active.Values.Where(held => held.ExpiresAt is { } until && now >= until).ToArray();
        foreach (var held in ended) _active.Remove(held.TargetId);
        return ended;
    }
}
