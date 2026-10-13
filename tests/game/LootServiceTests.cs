using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class LootServiceTests
{
    private static CreatureLootTableDefinition OneGuaranteedItem(uint itemId, int minCount = 1, int maxCount = 1) =>
        new(9001, [new LootGroupDefinition(1, 1, [new LootTableItemDefinition(itemId, 100, minCount, maxCount)])]);

    [Fact]
    public void Generate_WithNoLootTable_HasNoItemRowsButStillFallsBackToTheFlatPurse()
    {
        var loot = new LootService().Generate(2802);

        Assert.Equal(LootService.CorpseMoney, loot.Money);
        Assert.Empty(loot.Rows);
    }

    [Fact]
    public void Generate_WithAGuaranteedSingleItem_AlwaysAddsThatItem()
    {
        var table = OneGuaranteedItem(itemId: 6689);
        var loot = new LootService(nextPercent: () => 0).Generate(2802, table);

        var row = Assert.Single(loot.Rows);
        Assert.Equal(0, row.Slot);
        Assert.Equal(6689, row.Entry);
        Assert.Equal(1, row.Count);
    }

    [Fact]
    public void Generate_RollsAnItemsCountWithinItsOwnRange()
    {
        var table = OneGuaranteedItem(itemId: 6689, minCount: 2, maxCount: 5);
        var loot = new LootService(rollRange: (_, _) => 4, nextPercent: () => 0).Generate(2802, table);

        Assert.Equal(4, Assert.Single(loot.Rows).Count);
    }

    [Fact]
    public void Generate_DoesNotRollAnItemsCountWhenItsRangeHasNothingInIt()
    {
        var table = OneGuaranteedItem(itemId: 6689, minCount: 3, maxCount: 3);
        var loot = new LootService(
            rollRange: (_, _) => throw new InvalidOperationException("should not roll"),
            nextPercent: () => 0).Generate(2802, table);

        Assert.Equal(3, Assert.Single(loot.Rows).Count);
    }

    private static LootService Seeded(int seed)
    {
        var random = new Random(seed);
        return new LootService(rollRange: (min, max) => random.Next(min, max + 1), nextPercent: () => random.NextDouble() * 100.0);
    }

    private static CreatureLootTableDefinition Group(int guaranteed, int max, params (uint Id, double Chance)[] items) =>
        new(2802, [new LootGroupDefinition(guaranteed, max, items.Select(item => new LootTableItemDefinition(item.Id, item.Chance, 1, 1)).ToList())]);

    [Fact]
    public void Generate_OverManyKills_EachItemDropsAtItsOwnChance()
    {
        var table = Group(0, 0, (101, 25), (102, 75), (103, 5));
        var service = Seeded(1);
        var drops = new Dictionary<int, int> { [101] = 0, [102] = 0, [103] = 0 };

        for (var kill = 0; kill < 10_000; kill++)
        {
            foreach (var row in service.Generate(2802, table).Rows) drops[row.Entry]++;
        }

        Assert.InRange(drops[101], 2_300, 2_700);
        Assert.InRange(drops[102], 7_300, 7_700);
        Assert.InRange(drops[103], 400, 600);
    }

    [Fact]
    public void Generate_OverManyKills_AGroupWithAMinimumAndMaximumOfOneAlwaysDropsOneFavouringTheLikelierItem()
    {
        var table = Group(1, 1, (101, 25), (102, 75));
        var service = Seeded(2);
        var drops = new Dictionary<int, int> { [101] = 0, [102] = 0 };

        for (var kill = 0; kill < 10_000; kill++)
        {
            drops[Assert.Single(service.Generate(2802, table).Rows).Entry]++;
        }

        Assert.True(drops[102] > 2 * drops[101], $"101: {drops[101]}, 102: {drops[102]}");
    }

    [Fact]
    public void Generate_WhenEveryItemMisses_TopsTheGroupUpWithTheNearestMiss()
    {
        var table = Group(1, 0, (101, 25), (102, 75));

        var closeFor101 = new Queue<double>([30, 99]);
        Assert.Equal(101, Assert.Single(new LootService(nextPercent: closeFor101.Dequeue).Generate(2802, table).Rows).Entry);

        var closeFor102 = new Queue<double>([99, 80]);
        Assert.Equal(102, Assert.Single(new LootService(nextPercent: closeFor102.Dequeue).Generate(2802, table).Rows).Entry);
    }

    [Fact]
    public void Generate_OverTheGroupMaximum_KeepsTheItemsThatBeatTheirChanceByTheMost()
    {
        var table = Group(0, 1, (101, 50), (102, 50), (103, 50));

        var rolls = new Queue<double>([40, 10, 20]);
        var loot = new LootService(nextPercent: rolls.Dequeue).Generate(2802, table);

        Assert.Equal(102, Assert.Single(loot.Rows).Entry);
    }

    [Fact]
    public void Generate_AnItemWithNoChanceNeverDrops_EvenToMeetAMinimum()
    {
        var table = Group(2, 0, (101, 0), (102, 100));

        var loot = new LootService(nextPercent: () => 0).Generate(2802, table);

        Assert.Equal(102, Assert.Single(loot.Rows).Entry);
    }

    [Fact]
    public void Generate_KeepsRowsInTheGroupsOwnItemOrder()
    {
        var table = Group(0, 0, (101, 100), (102, 100), (103, 100));

        var loot = new LootService(nextPercent: () => 0).Generate(2802, table);

        Assert.Equal([101, 102, 103], loot.Rows.Select(row => row.Entry));
        Assert.Equal([0, 1, 2], loot.Rows.Select(row => row.Slot));
    }

    [Fact]
    public void Generate_GuaranteedDropsBeyondTheGroupsOwnItemCount_StopsAtWhatExists()
    {
        var table = new CreatureLootTableDefinition(2802,
            [new LootGroupDefinition(GuaranteedDrops: 2, MaxDrops: 2, [new LootTableItemDefinition(6689, 100, 1, 1)])]);

        var loot = new LootService(nextPercent: () => 0).Generate(2802, table);

        Assert.Single(loot.Rows);
    }

    [Fact]
    public void Generate_NeverAddsMoreThanTheCorpseRowCap()
    {
        var groups = Enumerable.Range(1, LootService.MaxLootRowsPerCorpse + 3)
            .Select(itemId => new LootGroupDefinition(1, 1, [new LootTableItemDefinition((uint)itemId, 100, 1, 1)]))
            .ToList();
        var table = new CreatureLootTableDefinition(2802, groups);

        var loot = new LootService(nextPercent: () => 0).Generate(2802, table);

        Assert.Equal(LootService.MaxLootRowsPerCorpse, loot.Rows.Count);
    }

    [Fact]
    public void Take_RemovesOnlyTheNamedRow()
    {
        var service = new LootService(nextPercent: () => 0);
        var loot = service.Generate(9001, OneGuaranteedItem(6689));

        var (row, remaining) = service.Take(loot, 0);

        Assert.NotNull(row);
        Assert.Empty(remaining.Rows);
        Assert.Equal(LootService.CorpseMoney, remaining.Money);
    }

    [Fact]
    public void Take_UnknownSlotKeepsEverything()
    {
        var service = new LootService(nextPercent: () => 0);
        var loot = service.Generate(9001, OneGuaranteedItem(6689));

        var (row, remaining) = service.Take(loot, 4);

        Assert.Null(row);
        Assert.Single(remaining.Rows);
    }

    [Fact]
    public void TakeMoney_EmptiesThePurseOnce()
    {
        var service = new LootService();
        var loot = service.Generate(9001);

        var (amount, remaining) = service.TakeMoney(loot);

        Assert.Equal(LootService.CorpseMoney, amount);
        Assert.Equal(0, remaining.Money);
        Assert.Equal(0, service.TakeMoney(remaining).Amount);
    }

    [Fact]
    public void IsEmpty_RequiresNoCoinAndNoRows()
    {
        var service = new LootService(nextPercent: () => 0);
        var loot = service.Generate(9001, OneGuaranteedItem(6689));

        Assert.False(LootService.IsEmpty(loot));
        var (_, noMoney) = service.TakeMoney(loot);
        Assert.False(LootService.IsEmpty(noMoney));
        var (_, emptied) = service.Take(noMoney, 0);
        Assert.True(LootService.IsEmpty(emptied));
    }

    [Fact]
    public void IsEmpty_TrueForACreatureWithNeitherMoneyNorAnItemTable()
    {
        var loot = new LootService().Generate(9001, lootTable: null, moneyMin: 0, moneyMax: 0);

        Assert.True(LootService.IsEmpty(loot));
    }

    [Fact]
    public void Generate_RollsMoneyWithinTheGivenRangeWhenOneIsGiven()
    {
        var loot = new LootService(rollRange: (_, max) => max).Generate(9001, moneyMin: 20, moneyMax: 80);

        Assert.Equal(80, loot.Money);
    }

    [Fact]
    public void Generate_FallsBackToTheFlatPurseWithoutARange()
    {
        var loot = new LootService().Generate(9001);

        Assert.Equal(LootService.CorpseMoney, loot.Money);
    }

    [Fact]
    public void Generate_DoesNotRollMoneyWhenTheRangeHasNothingInIt()
    {
        var loot = new LootService(rollRange: (_, _) => throw new InvalidOperationException("should not roll"))
            .Generate(9001, moneyMin: 30, moneyMax: 30);

        Assert.Equal(30, loot.Money);
    }
}
