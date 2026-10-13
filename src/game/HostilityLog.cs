namespace Ubiquitous.Game;

public interface IHostilityLog
{
    void Add(string mobId, string attackerId, double amount);
    IReadOnlyDictionary<string, double> For(string mobId);
    void Clear(string mobId);
    string? Victim(string mobId);
    void SetVictim(string mobId, string attackerId);
    void ClearVictim(string mobId);
    void AddFighter(string mobId, string attackerId);
    void RemoveFighter(string mobId, string attackerId);
    void ClearFighters(string mobId);
    IReadOnlyCollection<string> Fighters(string mobId);
}

public sealed class LocalHostilityLog : IHostilityLog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, double>> _table = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _victims = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _fighters = new(StringComparer.Ordinal);
    private static readonly TimeSpan FighterExpiry = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _time;

    public LocalHostilityLog(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void Add(string mobId, string attackerId, double amount)
    {
        lock (_gate)
        {
            if (!_table.TryGetValue(mobId, out var entries))
            {
                entries = new Dictionary<string, double>(StringComparer.Ordinal);
                _table[mobId] = entries;
            }
            entries[attackerId] = entries.TryGetValue(attackerId, out var held) ? held + amount : amount;
        }
    }

    public IReadOnlyDictionary<string, double> For(string mobId)
    {
        lock (_gate)
            return _table.TryGetValue(mobId, out var entries)
                ? new Dictionary<string, double>(entries, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);
    }

    public void Clear(string mobId)
    {
        lock (_gate)
        {
            _table.Remove(mobId);
            _victims.Remove(mobId);
            _fighters.Remove(mobId);
        }
    }

    public string? Victim(string mobId)
    {
        lock (_gate) return _victims.TryGetValue(mobId, out var victim) ? victim : null;
    }

    public void SetVictim(string mobId, string attackerId)
    {
        lock (_gate) _victims[mobId] = attackerId;
    }

    public void ClearVictim(string mobId)
    {
        lock (_gate) _victims.Remove(mobId);
    }

    public void AddFighter(string mobId, string attackerId)
    {
        lock (_gate)
        {
            if (!_fighters.TryGetValue(mobId, out var set))
            {
                set = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                _fighters[mobId] = set;
            }
            set[attackerId] = _time.GetUtcNow();
        }
    }

    public void RemoveFighter(string mobId, string attackerId)
    {
        lock (_gate)
        {
            if (_fighters.TryGetValue(mobId, out var set))
            {
                set.Remove(attackerId);
                if (set.Count == 0) _fighters.Remove(mobId);
            }
        }
    }

    public void ClearFighters(string mobId)
    {
        lock (_gate) _fighters.Remove(mobId);
    }

    public IReadOnlyCollection<string> Fighters(string mobId)
    {
        lock (_gate)
        {
            if (!_fighters.TryGetValue(mobId, out var set)) return Array.Empty<string>();
            var cutoff = _time.GetUtcNow() - FighterExpiry;
            return set.Where(pair => pair.Value >= cutoff).Select(pair => pair.Key).ToArray();
        }
    }
}
