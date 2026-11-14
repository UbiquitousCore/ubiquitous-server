using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class StartingSpellbookTests
{
    private const uint Bow = 16;
    private const uint Wand = 20;

    // A later build in miniature: general spells, two Auto Shoots, a mage chain taught from
    // level 2, a second from 3, a third from 9, and a Flame Knight lesson at 65.
    public static IReadOnlyDictionary<uint, SpellDefinition> Spells { get; } = new SpellDefinition[]
    {
        new(1, "Gathering", 2000, 1500, 3.6f),
        new(2, "Gathering", 3500, 1500, 5f),
        new(3, "Gathering", 2000, 1500, 3.6f),
        new(4, "Switch Target", 0, 0, 60f),
        new(5, "Auto Attack", 0, 3000, 0f),
        new(6, "Auto Shoot", 0, 3000, 25f, WornSlotNeeded: EquipmentSlots.Ranged, RequiredItemClass: 2,
            ItemSubclassesAllowed: 1u << (int)Bow),
        new(7, "Auto Shoot", 0, 3000, 25f, WornSlotNeeded: EquipmentSlots.Ranged, RequiredItemClass: 2,
            ItemSubclassesAllowed: 1u << (int)Wand),
        new(40, "Riding Permit for Humans", 0, 0, 0f),
        new(41, "Riding Permit for Mendel", 0, 0, 0f),
        new(100, "Fireball", 2000, 0, 25f, Rank: 1),
        new(101, "Fireball", 2000, 0, 25f, Rank: 2, ChainFirstSpellId: 100),
        new(110, "Frost Bolt", 1500, 0, 25f, Rank: 1),
        new(111, "Frost Bolt", 1500, 0, 25f, Rank: 2, ChainFirstSpellId: 110),
        new(120, "Flame Storm", 0, 0, 25f, Rank: 1),
        new(121, "Flame Storm", 0, 0, 25f, Rank: 2, ChainFirstSpellId: 120),
        new(200, "Swift Strike", 0, 0, 0f, Rank: 1),
        new(201, "Swift Strike", 0, 0, 0f, Rank: 2, ChainFirstSpellId: 200),
        new(300, "Metamorphosis", 0, 0, 0f, Rank: 1),
        new(901, "Learn Fireball II", 0, 0, 0f, EffectLearnSpellId: 101),
        new(911, "Learn Frost Bolt II", 0, 0, 0f, EffectLearnSpellId: 111),
        new(921, "Learn Flame Storm II", 0, 0, 0f, EffectLearnSpellId: 121),
        new(931, "Learn Swift Strike II", 0, 0, 0f, EffectLearnSpellId: 201),
        new(941, "Learn Metamorphosis", 0, 0, 0f, EffectLearnSpellId: 300),
    }.ToDictionary(spell => spell.Id);

    private static ItemDefinition Lesson(uint id, uint spell, int playerClass, uint level) =>
        new(id, $"lesson-{id}", Class: 9, SubClass: 0, DisplayInfoId: 0, OnUseSpellId: spell,
            AllowableClassMask: ItemClassMask.BitFor(playerClass), RequiredLevel: level);

    private static ItemDefinition RangedWeapon(uint id, uint subClass, int playerClass) =>
        new(id, $"ranged-{id}", Class: ItemClasses.Weapons, SubClass: (byte)subClass, DisplayInfoId: 0,
            AllowableClassMask: ItemClassMask.BitFor(playerClass), InventoryType: InventoryTypes.Ranged);

    public static IReadOnlyDictionary<uint, ItemDefinition> Items { get; } = new[]
    {
        Lesson(5001, 901, PlayerClassIds.Mage, 2),
        Lesson(5002, 911, PlayerClassIds.Mage, 3),
        Lesson(5003, 921, PlayerClassIds.Mage, 9),
        Lesson(5004, 931, PlayerClassIds.Warrior, 2),
        Lesson(5005, 941, PlayerClassIds.FlameKnight, 65),
        RangedWeapon(6001, Bow, PlayerClassIds.Warrior),
        RangedWeapon(6002, Wand, PlayerClassIds.Mage),
        RangedWeapon(6003, Wand, PlayerClassIds.FlameKnight),
        new(7001, "flame-knight-blade", Class: ItemClasses.Weapons, SubClass: 8, DisplayInfoId: 0,
            AllowableClassMask: ItemClassMask.BitFor(PlayerClassIds.FlameKnight),
            InventoryType: InventoryTypes.TwoHand, RequiredLevel: 65),
        new(7002, "everyones-blade", Class: ItemClasses.Weapons, SubClass: 8, DisplayInfoId: 0,
            InventoryType: InventoryTypes.TwoHand),
    }.ToDictionary(item => item.Id);

    // Spell 1 on nearly every type, 2 on the ore node, 3 on chests, 9 on one odd object.
    public static IReadOnlyList<StaticObjectTypeDefinition> Types { get; } =
    [
        .. Enumerable.Range(1000, 110).Select(entry => new StaticObjectTypeDefinition((uint)entry, 6, GatherSpellId: 1)),
        new(MiningOres.Bronze.NodeEntry, 1, GatherSpellId: 2),
        new(MiningOres.Marcasite.NodeEntry, 1, GatherSpellId: 2),
        .. Enumerable.Range(2000, 4).Select(entry => new StaticObjectTypeDefinition((uint)entry, 5, GatherSpellId: 3)),
        new(3000, 5, GatherSpellId: 9),
        new(3001, 5),
    ];

    public static IReadOnlyDictionary<int, int> Levels { get; } = new Dictionary<int, int>
    {
        [PlayerClassIds.Warrior] = 1,
        [PlayerClassIds.Mage] = 1,
        [PlayerClassIds.FlameKnight] = 65,
    };

    private static StartingSpellbook Book() => StartingSpellbook.Read(Spells, Items, Types, Levels);

    [Fact]
    public void TheGeneralSpells_AreFoundByTheirNamesAndTheGatherSpellsMostObjectsName()
    {
        var book = Book();

        Assert.Equal(5u, book.AutoAttack);
        Assert.Equal(4u, book.SwitchTarget);
        Assert.Equal([1u, 2u, 3u], book.Gathering);
        Assert.Equal(2u, book.MiningGather);
    }

    [Fact]
    public void EachClass_ShootsWithTheAutoShootThatFitsTheRangedWeaponsItMayUse()
    {
        var book = Book();

        Assert.Equal(6u, book.Classes[PlayerClassIds.Warrior].AutoShoot);
        Assert.Equal(7u, book.Classes[PlayerClassIds.Mage].AutoShoot);
        Assert.Equal(7u, book.Classes[PlayerClassIds.FlameKnight].AutoShoot);
    }

    [Fact]
    public void ALevelOneClass_StartsWithTheFirstRankOfEveryChainTaughtSoonAfter()
    {
        var mage = Book().Classes[PlayerClassIds.Mage];

        Assert.Equal([100u, 110u], mage.ClassSpells);
        Assert.Equal(100u, mage.Opener);
    }

    [Fact]
    public void AClassStartingHigher_HoldsWhatItsLessonsTeachByThatLevel()
    {
        var knight = Book().Classes[PlayerClassIds.FlameKnight];

        Assert.Equal([300u], knight.ClassSpells);
    }

    [Fact]
    public void TheSpellbook_IsTheClassSpellsThenTheGeneralOnesThenTheRacesPermit()
    {
        var book = Book();

        Assert.Equal([100u, 110u, 5u, 7u, 4u, 1u, 2u, 3u, 40u], book.SpellsFor(PlayerRaceIds.Human, PlayerClassIds.Mage));
        Assert.Equal([300u, 5u, 7u, 4u, 1u, 2u, 3u, 41u], book.SpellsFor(PlayerRaceIds.Mendel, PlayerClassIds.FlameKnight));
        Assert.Equal([200u, 5u, 6u, 4u, 1u, 2u, 3u], book.SpellsFor(PlayerRaceIds.Orc, PlayerClassIds.Warrior));
    }

    [Fact]
    public void AMissingGeneralSpell_StopsTheRead_NamingIt()
    {
        var spells = Spells.Where(pair => pair.Value.NameKey != "Auto Attack").ToDictionary();

        var thrown = Assert.Throws<InvalidOperationException>(() => StartingSpellbook.Read(spells, Items, Types, Levels));

        Assert.Contains("Auto Attack", thrown.Message);
    }

    [Fact]
    public void TheBuildsLoadouts_KeepTheSeenKitsAndAddAFlameKnightOnFlareIsland()
    {
        var loadouts = StartingLoadouts.FromSpellbook(Book(), Items);

        var mage = StartingLoadouts.For(loadouts, PlayerRaceIds.Elf, PlayerClassIds.Mage)!;
        Assert.Equal(StartingLoadouts.For(PlayerRaceIds.Elf, PlayerClassIds.Mage)!.Items, mage.Items);
        Assert.Equal([new StartingActionButton(0, 5), new StartingActionButton(1, 100)], mage.ActionBar);
        Assert.Equal(1, mage.Level);
        Assert.Null(StartingLoadouts.For(loadouts, PlayerRaceIds.Elf, PlayerClassIds.Ranger));

        var knight = StartingLoadouts.For(loadouts, PlayerRaceIds.Orc, PlayerClassIds.FlameKnight)!;
        Assert.Equal(StartingLoadouts.FlameKnightStartingLevel, knight.Level);
        Assert.Equal(38, knight.MapId);
        Assert.Contains(knight.Items, item => item is { ItemEntry: 7001, Slot: EquipmentSlots.MainHand });
        Assert.Equal(StartingLoadouts.For(PlayerRaceIds.Orc, PlayerClassIds.Warrior)!.Attributes, knight.Attributes);
        Assert.Equal(PlayerRaceIds.All.Count, loadouts.Count(loadout => loadout.PlayerClass == PlayerClassIds.FlameKnight));
    }
}
