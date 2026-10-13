namespace Ubiquitous.Game;

public sealed class MiningPool
{
    private readonly IReadOnlyList<uint> _locations;
    private readonly IReadOnlyList<MiningOreWeight> _ores;
    private readonly Func<double> _rollUnit;
    private readonly Dictionary<uint, MiningOre> _active = new();
    private readonly Dictionary<uint, DateTimeOffset> _idleSince = new();

    public MiningPool(IEnumerable<uint> locations, IReadOnlyList<MiningOreWeight> ores, Func<double> rollUnit)
    {
        _locations = [.. locations.Order()];
        _ores = ores;
        _rollUnit = rollUnit;
        if (_ores.Count == 0) return;
        var wanted = MiningRules.ActiveCount(_locations.Count);
        for (var count = 0; count < wanted; count++) ActivateOneOf(Idle());
    }

    public long Version { get; private set; }

    public int ActiveCount => _active.Count;

    public bool Contains(uint location) => _locations.Contains(location);

    public bool TryGetActive(uint location, out MiningOre ore) => _active.TryGetValue(location, out ore!);

    public MiningOre? Mine(uint location, DateTimeOffset now)
    {
        if (!_active.Remove(location, out var ore)) return null;
        _idleSince[location] = now;
        Version++;
        var candidates = Idle();
        var recovered = candidates.Where(candidate => CooledDown(candidate, now)).ToArray();
        ActivateOneOf(recovered.Length > 0 ? recovered : [LongestIdle(candidates)]);
        return ore;
    }

    private IReadOnlyList<uint> Idle() => [.. _locations.Where(location => !_active.ContainsKey(location))];

    private bool CooledDown(uint location, DateTimeOffset now) =>
        !_idleSince.TryGetValue(location, out var since) || now - since >= MiningRules.LocationCooldown;

    private uint LongestIdle(IReadOnlyList<uint> candidates) =>
        candidates.OrderBy(location => _idleSince.GetValueOrDefault(location, DateTimeOffset.MinValue)).First();

    private void ActivateOneOf(IReadOnlyList<uint> candidates)
    {
        if (candidates.Count == 0) return;
        var chosen = candidates[Math.Min((int)(_rollUnit() * candidates.Count), candidates.Count - 1)];
        _active[chosen] = RollOre();
        Version++;
    }

    private MiningOre RollOre()
    {
        var total = _ores.Sum(entry => entry.Weight);
        var point = _rollUnit() * total;
        foreach (var entry in _ores)
        {
            point -= entry.Weight;
            if (point < 0) return entry.Ore;
        }

        return _ores[^1].Ore;
    }
}
