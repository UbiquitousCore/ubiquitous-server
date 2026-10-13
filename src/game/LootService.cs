using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record LootRow(int Slot, string ItemId, int Entry, int Count);
public sealed record CorpseLoot(int Money, IReadOnlyList<LootRow> Rows);

public sealed class LootService
{
    public const int CorpseMoney = 50;

    public const int MaxLootRowsPerCorpse = 8;

    private readonly Func<int, int, int> _rollRange;
    private readonly Func<double> _nextPercent;

    public LootService(Func<int, int, int>? rollRange = null, Func<double>? nextPercent = null)
    {
        _rollRange = rollRange ?? ((min, max) => Random.Shared.Next(min, max + 1));
        _nextPercent = nextPercent ?? (() => Random.Shared.NextDouble() * 100.0);
    }

    public CorpseLoot Generate(int entry, CreatureLootTableDefinition? lootTable = null, int? moneyMin = null, int? moneyMax = null)
    {
        var money = moneyMin is not null && moneyMax is not null
            ? Roll(moneyMin.Value, moneyMax.Value)
            : CorpseMoney;

        var rows = new List<LootRow>();
        foreach (var group in lootTable?.Groups ?? [])
        {
            foreach (var item in RollGroup(group))
            {
                if (rows.Count == MaxLootRowsPerCorpse) return new(money, rows);
                var slot = rows.Count;
                rows.Add(new LootRow(slot, $"loot-{item.ItemId}-{slot}", checked((int)item.ItemId), Roll(item.MinDropCount, item.MaxDropCount)));
            }
        }

        return new(money, rows);
    }

    public static CorpseLoot Gathered(uint itemId, int count) =>
        new(0, [new LootRow(0, $"loot-{itemId}-0", checked((int)itemId), count)]);

    private IReadOnlyList<LootTableItemDefinition> RollGroup(LootGroupDefinition group)
    {
        var margins = group.Items
            .Select(item => item.DropChancePercent > 0
                ? (item.DropChancePercent - _nextPercent()) / item.DropChancePercent
                : double.NegativeInfinity)
            .ToArray();

        var dropped = Enumerable.Range(0, margins.Length).Where(index => margins[index] > 0).ToHashSet();

        var shortBy = group.GuaranteedDrops - dropped.Count;
        if (shortBy > 0)
        {
            var nearestMisses = Enumerable.Range(0, margins.Length)
                .Where(index => !dropped.Contains(index) && !double.IsNegativeInfinity(margins[index]))
                .OrderByDescending(index => margins[index])
                .Take(shortBy);
            dropped.UnionWith(nearestMisses);
        }

        if (group.MaxDrops > 0 && dropped.Count > group.MaxDrops)
        {
            dropped = dropped.OrderByDescending(index => margins[index]).Take(group.MaxDrops).ToHashSet();
        }

        return dropped.Order().Select(index => group.Items[index]).ToArray();
    }

    private int Roll(int min, int max) => min >= max ? min : _rollRange(min, max);

    public (LootRow? Row, CorpseLoot Remaining) Take(CorpseLoot loot, int slot)
    {
        var row = loot.Rows.FirstOrDefault(candidate => candidate.Slot == slot);
        if (row is null) return (null, loot);
        return (row, loot with { Rows = loot.Rows.Where(candidate => candidate.Slot != slot).ToArray() });
    }

    public (int Amount, CorpseLoot Remaining) TakeMoney(CorpseLoot loot)
    {
        if (loot.Money <= 0) return (0, loot);
        return (loot.Money, loot with { Money = 0 });
    }

    public static bool IsEmpty(CorpseLoot loot) => loot.Money <= 0 && loot.Rows.Count == 0;
}
