namespace Ubiquitous.Game;

public sealed class DirtyValueTracker
{
    public const double WindowMilliseconds = 500d;

    private sealed class Batch
    {
        public required DateTimeOffset FirstDirty;
        public readonly Dictionary<string, int> Fields = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Batch> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _sent = new(StringComparer.Ordinal);

    public void Mark(string unitId, string field, int value, DateTimeOffset now)
    {
        if (_sent.TryGetValue(unitId, out var sent)
            && sent.TryGetValue(field, out var last)
            && last == value
            && !_pending.TryGetValue(unitId, out _))
            return;

        if (!_pending.TryGetValue(unitId, out var batch))
        {
            batch = new Batch { FirstDirty = now };
            _pending[unitId] = batch;
        }
        batch.Fields[field] = value;
    }

    public void MarkAll(string unitId, IReadOnlyDictionary<string, int> changed, DateTimeOffset now)
    {
        foreach (var (field, value) in changed) Mark(unitId, field, value, now);
    }

    public IReadOnlyList<(string UnitId, IReadOnlyDictionary<string, int> Changed)> FlushDue(DateTimeOffset now)
    {
        var due = new List<(string UnitId, IReadOnlyDictionary<string, int> Changed)>();
        foreach (var (unitId, batch) in _pending.ToArray())
        {
            if ((now - batch.FirstDirty).TotalMilliseconds < WindowMilliseconds) continue;
            _pending.Remove(unitId);
            var changed = new Dictionary<string, int>();
            if (!_sent.TryGetValue(unitId, out var sent))
            {
                sent = new Dictionary<string, int>(StringComparer.Ordinal);
                _sent[unitId] = sent;
            }
            foreach (var (field, value) in batch.Fields)
            {
                if (sent.TryGetValue(field, out var last) && last == value) continue;
                sent[field] = value;
                changed[field] = value;
            }
            if (changed.Count > 0) due.Add((unitId, changed));
        }
        due.Sort(static (a, b) => string.CompareOrdinal(a.UnitId, b.UnitId));
        return due;
    }

    public void MarkSent(string unitId, IReadOnlyDictionary<string, int> values)
    {
        if (!_sent.TryGetValue(unitId, out var sent))
        {
            sent = new Dictionary<string, int>(StringComparer.Ordinal);
            _sent[unitId] = sent;
        }
        _pending.TryGetValue(unitId, out var batch);
        foreach (var (field, value) in values)
        {
            sent[field] = value;
            batch?.Fields.Remove(field);
        }
        if (batch is not null && batch.Fields.Count == 0) _pending.Remove(unitId);
    }

    public void Clear()
    {
        _pending.Clear();
        _sent.Clear();
    }
}
