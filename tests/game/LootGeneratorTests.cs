using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class LootGeneratorTests
{
    private const uint Potion = 300;
    private const uint Heal = 30;

    private static CreatureDefinition Creature(uint entry, int level, CreatureRank rank = CreatureRank.Ordinary,
        int faction = UnitFactionTypes.Hostile) =>
        new(entry, $"creature {entry}", level, 100, FactionType: faction, Rank: rank);

    private static ItemDefinition Gear(uint id, uint level, uint quality, string? name = null, uint inventoryType = InventoryTypes.Chest) =>
        new(id, name ?? $"gear {id}", ItemClasses.Armour, 1, 0, SellPrice: 10, InventoryType: inventoryType,
            RequiredLevel: level, Quality: quality);

    private static readonly IReadOnlyDictionary<uint, SpellDefinition> Spells = new Dictionary<uint, SpellDefinition>
    {
        [Heal] = new(Heal, "Heal", 0, 0, 0f, EffectHeal: 50, EffectTarget: SpellEffectTargets.Self),
    };

    private static GeneratedLoot Build(
        IEnumerable<CreatureDefinition> creatures, IEnumerable<ItemDefinition> items,
        IReadOnlyList<QuestItemSourceDefinition>? sources = null,
        IReadOnlyDictionary<uint, NpcDefinition>? npcs = null,
        IReadOnlyDictionary<uint, QuestRewardDefinition>? rewards = null,
        IReadOnlyDictionary<uint, IReadOnlyList<VendorTab>>? vendors = null) =>
        LootGenerator.Build(
            creatures.ToDictionary(creature => creature.EntryId),
            items.ToDictionary(item => item.Id),
            Spells,
            npcs ?? new Dictionary<uint, NpcDefinition>(),
            rewards ?? new Dictionary<uint, QuestRewardDefinition>(),
            vendors ?? new Dictionary<uint, IReadOnlyList<VendorTab>>(),
            sources ?? [],
            LootGeneratorRules.Default);

    private static IEnumerable<uint> ItemsOf(GeneratedLoot loot, uint entry) =>
        loot.Tables[entry].Groups.SelectMany(group => group.Items).Select(item => item.ItemId);

    [Fact]
    public void AnOrdinaryCreature_DropsLowQualityGearAskedForAtOrJustBelowItsLevel()
    {
        var loot = Build([Creature(1, 20)],
            [Gear(10, 20, 2), Gear(11, 16, 3), Gear(12, 15, 2), Gear(13, 21, 2), Gear(14, 20, 4)]);

        Assert.Equal([10u, 11u], ItemsOf(loot, 1).Order());
        var group = Assert.Single(loot.Tables[1].Groups);
        Assert.Equal((0, 1), (group.GuaranteedDrops, group.MaxDrops));
        Assert.Equal(8, group.Items.Sum(item => item.DropChancePercent), 6);
    }

    [Fact]
    public void ABoss_AlwaysDropsGear_OfTheHighestQualities()
    {
        var loot = Build([Creature(1, 20, CreatureRank.Boss)], [Gear(10, 20, 3), Gear(11, 19, 4), Gear(12, 18, 5)]);

        Assert.Equal([11u, 12u], ItemsOf(loot, 1).Order());
        Assert.Equal((1, 2), (loot.Tables[1].Groups[0].GuaranteedDrops, loot.Tables[1].Groups[0].MaxDrops));
    }

    [Fact]
    public void WithNothingAtItsLevel_ACreatureLooksTwiceAsFarBelow_ThenGoesWithout()
    {
        var loot = Build([Creature(1, 20), Creature(2, 40)], [Gear(10, 12, 2)]);

        Assert.Equal([10u], ItemsOf(loot, 1));
        Assert.False(loot.Tables.ContainsKey(2));
    }

    [Fact]
    public void GearSoldGivenOrBoundToAQuest_OrWornNowhere_OrATestRow_IsNotDropped()
    {
        var rewards = new Dictionary<uint, QuestRewardDefinition>
        {
            [1] = new(1, 0, [new QuestRewardItem(11, 1, 0, 2)], []),
        };
        var vendors = new Dictionary<uint, IReadOnlyList<VendorTab>>
        {
            [500] = [new VendorTab(0, 0, 1, true, [new VendorItem(0, 12, 0, 0, 10, 0, 1)])],
        };
        var loot = Build([Creature(1, 20)],
            [
                Gear(10, 20, 2), Gear(11, 20, 2), Gear(12, 20, 2), Gear(13, 20, 2) with { QuestId = 4 },
                Gear(14, 20, 2, inventoryType: 23), Gear(15, 20, 2, name: "*test sword"), Gear(16, 20, 2) with { SellPrice = 0 },
                Gear(17, 20, 2) with { Quality = 2147483647 },
            ],
            rewards: rewards, vendors: vendors);

        Assert.Equal([10u], ItemsOf(loot, 1));
    }

    [Fact]
    public void TownsfolkAndServiceCreatures_CarryNoLoot()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [2] = new(2, NpcFlags.Vendor) };

        var loot = Build([Creature(1, 20, faction: UnitFactionTypes.Townsfolk), Creature(2, 20), Creature(3, 20, faction: UnitFactionTypes.Neutral)],
            [Gear(10, 20, 2)], npcs: npcs);

        Assert.Equal([3u], loot.Tables.Keys);
    }

    [Fact]
    public void AQuestItem_DropsFromTheCreaturesItsQuestNames_AndIsRememberedForThatQuest()
    {
        var item = new ItemDefinition(50, "Wolf Pelt", 10, 0, 0);

        var loot = Build([Creature(1, 5), Creature(2, 5)], [item], [new QuestItemSourceDefinition(7, 50, [1])]);

        var group = Assert.Single(loot.Tables[1].Groups);
        Assert.Equal(new LootTableItemDefinition(50, 40, 1, 1), Assert.Single(group.Items));
        Assert.False(loot.Tables.ContainsKey(2));
        Assert.Equal([7u], loot.QuestItems[50]);
    }

    [Fact]
    public void FoodAndPotions_FallByLevel()
    {
        var potion = new ItemDefinition(Potion, "Healing Potion", ItemClasses.Consumables, 1, 0, OnUseSpellId: Heal,
            SellPrice: 5, RequiredLevel: 12, Quality: 2);

        var loot = Build([Creature(1, 15), Creature(2, 11)], [potion]);

        Assert.Equal([Potion], ItemsOf(loot, 1));
        Assert.False(loot.Tables.ContainsKey(2));
    }

    [Fact]
    public void TheSameCreature_AlwaysDrawsTheSamePieces_FromAPoolLargerThanItsShare()
    {
        var pool = Enumerable.Range(100, 40).Select(id => Gear((uint)id, 20, 2)).ToArray();

        var first = Build([Creature(1, 20), Creature(2, 20)], pool);
        var again = Build([Creature(1, 20), Creature(2, 20)], pool);

        Assert.Equal(LootGeneratorRules.Default.GearPerCreature, ItemsOf(first, 1).Count());
        Assert.Equal(ItemsOf(first, 1), ItemsOf(again, 1));
        Assert.NotEqual(ItemsOf(first, 1), ItemsOf(first, 2));
    }
}
