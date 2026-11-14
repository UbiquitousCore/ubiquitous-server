using Ubiquitous.Data;

namespace Ubiquitous.Game;

// How much a creature of a rank drops of gear: the chance a body carries a piece, how many it always
// and at most carries, and the item qualities (1 poor to 5 epic) it draws from.
public sealed record RankGear(double ChancePercent, int Guaranteed, int Most, int LowestQuality, int HighestQuality);

// Every number here is this project's own choice: no table of a build without loot says what a
// creature drops. They are tuned so that a quest's named creatures give its items in a few kills,
// gear turns up now and then from ordinary creatures and always from bosses, and potions and food
// keep a player going.
public sealed record LootGeneratorRules(
    double QuestItemPercent = 40,
    int GearLevelWindow = 5,
    int GearPerCreature = 12,
    double ConsumablePercent = 10,
    int ConsumableLevelWindow = 10,
    int ConsumablesPerCreature = 4,
    IReadOnlyDictionary<CreatureRank, RankGear>? Gear = null)
{
    public IReadOnlyDictionary<CreatureRank, RankGear> Gear { get; init; } = Gear ?? new Dictionary<CreatureRank, RankGear>
    {
        [CreatureRank.Ordinary] = new(8, 0, 1, 1, 3),
        [CreatureRank.Rare] = new(40, 0, 1, 2, 4),
        [CreatureRank.Elite] = new(25, 0, 1, 3, 4),
        [CreatureRank.Boss] = new(100, 1, 2, 4, 5),
        [CreatureRank.WorldBoss] = new(100, 2, 3, 4, 5),
    };

    public static LootGeneratorRules Default { get; } = new();
}

// Loot tables made from the rest of a build's data, for a build whose tables carry none.
// `QuestItems` names, for each quest item a table holds, the quests it is dropped for.
public sealed record GeneratedLoot(
    IReadOnlyDictionary<uint, CreatureLootTableDefinition> Tables,
    IReadOnlyDictionary<uint, IReadOnlySet<uint>> QuestItems)
{
    public static GeneratedLoot None { get; } = new(
        new Dictionary<uint, CreatureLootTableDefinition>(), new Dictionary<uint, IReadOnlySet<uint>>());
}

public static class LootGenerator
{
    public static GeneratedLoot Build(
        IReadOnlyDictionary<uint, CreatureDefinition> creatures,
        IReadOnlyDictionary<uint, ItemDefinition> items,
        IReadOnlyDictionary<uint, SpellDefinition> spells,
        IReadOnlyDictionary<uint, NpcDefinition> npcs,
        IReadOnlyDictionary<uint, QuestRewardDefinition> rewards,
        IReadOnlyDictionary<uint, IReadOnlyList<VendorTab>> vendorTabs,
        IReadOnlyList<QuestItemSourceDefinition> questItemSources,
        LootGeneratorRules rules)
    {
        var rewarded = rewards.Values.SelectMany(reward => reward.Fixed.Concat(reward.Choices))
            .Select(item => item.Entry).ToHashSet();
        var sold = vendorTabs.Values.SelectMany(tabs => tabs).SelectMany(tab => tab.Items)
            .Select(item => (uint)item.Entry).ToHashSet();
        var gear = items.Values.Where(item => IsWorldGear(item, rewarded, sold))
            .OrderBy(item => item.RequiredLevel).ThenBy(item => item.Id).ToArray();
        var consumables = items.Values.Where(item => IsConsumable(item, spells))
            .OrderBy(item => item.RequiredLevel).ThenBy(item => item.Id).ToArray();

        var questDrops = questItemSources
            .SelectMany(source => source.CreatureEntryIds.Select(entry => (Entry: entry, source.ItemEntry, source.QuestId)))
            .ToLookup(drop => drop.Entry);
        var questItems = questItemSources.GroupBy(source => source.ItemEntry)
            .ToDictionary(group => group.Key, group => (IReadOnlySet<uint>)group.Select(source => source.QuestId).ToHashSet());

        var tables = new Dictionary<uint, CreatureLootTableDefinition>();
        foreach (var creature in creatures.Values.Where(creature => CarriesLoot(creature, npcs)))
        {
            var groups = new List<LootGroupDefinition>();
            var rank = rules.Gear.TryGetValue(creature.Rank, out var ranked) ? ranked : rules.Gear[CreatureRank.Ordinary];
            var guaranteedQuestDrop = rank.Guaranteed > 0;

            var carried = questDrops[creature.EntryId].Select(drop => drop.ItemEntry).Distinct().Order()
                .Where(items.ContainsKey)
                .Select(item => new LootTableItemDefinition(item, guaranteedQuestDrop ? 100 : rules.QuestItemPercent, 1, 1))
                .ToArray();
            if (carried.Length > 0) groups.Add(new LootGroupDefinition(0, 0, carried));

            var pieces = Pick(Within(gear, creature.Level, rules.GearLevelWindow,
                    item => item.Quality is uint quality && quality >= rank.LowestQuality && quality <= rank.HighestQuality),
                creature.EntryId, rules.GearPerCreature);
            if (pieces.Count > 0)
                groups.Add(new LootGroupDefinition(rank.Guaranteed, rank.Most, Shared(pieces, rank.ChancePercent)));

            var supplies = Pick(Within(consumables, creature.Level, rules.ConsumableLevelWindow, _ => true),
                creature.EntryId, rules.ConsumablesPerCreature);
            if (supplies.Count > 0)
                groups.Add(new LootGroupDefinition(0, 1, Shared(supplies, rules.ConsumablePercent)));

            if (groups.Count > 0) tables[creature.EntryId] = new CreatureLootTableDefinition(creature.EntryId, groups);
        }

        return new GeneratedLoot(tables, questItems);
    }

    // A creature townsfolk stand with, or one that serves players, is not fought.
    private static bool CarriesLoot(CreatureDefinition creature, IReadOnlyDictionary<uint, NpcDefinition> npcs) =>
        creature.Level > 0
        && creature.FactionType != UnitFactionTypes.Townsfolk
        && !(npcs.TryGetValue(creature.EntryId, out var npc)
            && (npc.OffersQuests || (npc.Flags & (NpcFlags.Vendor | NpcFlags.Teleporter | NpcFlags.Auctioneer)) != 0));

    // Gear found in the world: worn in a slot, of a level and a quality on the 1-5 scale, bought by a
    // vendor, and neither sold, given for a quest nor bound to one. The build's test rows are left out.
    private static bool IsWorldGear(ItemDefinition item, IReadOnlySet<uint> rewarded, IReadOnlySet<uint> sold) =>
        item.Class is ItemClasses.Weapons or ItemClasses.Armour
        && EquipSlotRules.SlotFor(item.InventoryType, _ => null) != EquipSlotRules.NoSlot
        && item.RequiredLevel > 0
        && item.Quality is >= 1 and <= 5
        && item.SellPrice is > 0
        && item.QuestId is null
        && !rewarded.Contains(item.Id) && !sold.Contains(item.Id)
        && IsPlayersItem(item);

    // Food, drink and potions: an item whose use eats, drinks, heals its user or gives power back.
    private static bool IsConsumable(ItemDefinition item, IReadOnlyDictionary<uint, SpellDefinition> spells) =>
        item.Class == ItemClasses.Consumables
        && item.RequiredLevel > 0
        && item.Quality is >= 1 and <= 5
        && item.SellPrice is > 0
        && IsPlayersItem(item)
        && item.OnUseSpellId is uint use && spells.TryGetValue(use, out var spell)
        && (spell.AuraEatPerTick is > 0 || spell.AuraDrinkPerTick is > 0 || spell.EffectPowerRestore is > 0
            || (spell.EffectHeal is > 0 && spell.EffectTarget == SpellEffectTargets.Self));

    private static bool IsPlayersItem(ItemDefinition item) =>
        !item.NameKey.StartsWith('*') && !item.NameKey.StartsWith("item-", StringComparison.Ordinal)
        && !item.NameKey.Contains("test", StringComparison.OrdinalIgnoreCase);

    // What a creature of this level may drop: the items asked for at its level or up to a window below,
    // else in a window twice as wide, else nothing.
    private static IReadOnlyList<ItemDefinition> Within(
        IReadOnlyList<ItemDefinition> pool, int level, int window, Func<ItemDefinition, bool> fits)
    {
        foreach (var width in new[] { window, window * 2 })
        {
            var lowest = level - width + 1;
            var found = pool.Where(item => item.RequiredLevel >= lowest && item.RequiredLevel <= level && fits(item)).ToArray();
            if (found.Length > 0) return found;
        }

        return [];
    }

    // The same creature always draws the same pieces, and two creatures of one level mostly different ones.
    private static IReadOnlyList<ItemDefinition> Pick(IReadOnlyList<ItemDefinition> pool, uint entry, int most) =>
        [.. pool.OrderBy(item => Mix(entry, item.Id)).ThenBy(item => item.Id).Take(most).OrderBy(item => item.Id)];

    private static LootTableItemDefinition[] Shared(IReadOnlyList<ItemDefinition> pieces, double chancePercent) =>
        [.. pieces.Select(item => new LootTableItemDefinition(item.Id, chancePercent / pieces.Count, 1, 1))];

    private static ulong Mix(uint entry, uint item)
    {
        var value = ((ulong)entry << 32) | item;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
