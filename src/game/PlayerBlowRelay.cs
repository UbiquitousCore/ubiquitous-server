using System.Collections.Concurrent;

namespace Ubiquitous.Game;

public enum PlayerBlowKind
{
    Swing,
    Shot,
    Spell,
    Heal,
    Aura,
    Harm,
}

public sealed record PlayerBlow(
    string AttackerId, string TargetId, int Amount, StrikeResult Result, PlayerBlowKind Kind,
    bool OffHand = false, uint SpellId = 0, int ExtraEffect = PlayerBlow.MainEffect)
{
    public const int MainEffect = -1;
}

public sealed class PlayerBlowRelay
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PlayerBlow>> _pending = new(StringComparer.Ordinal);

    public void Send(PlayerBlow blow) =>
        _pending.GetOrAdd(blow.TargetId, static _ => new ConcurrentQueue<PlayerBlow>()).Enqueue(blow);

    public IReadOnlyList<PlayerBlow> Drain(string targetId)
    {
        if (!_pending.TryGetValue(targetId, out var queue)) return [];
        var drained = new List<PlayerBlow>();
        while (queue.TryDequeue(out var blow)) drained.Add(blow);
        return drained;
    }

    public bool HasPending(string targetId) => _pending.TryGetValue(targetId, out var queue) && !queue.IsEmpty;

    public void Forget(string targetId) => _pending.TryRemove(targetId, out _);
}
