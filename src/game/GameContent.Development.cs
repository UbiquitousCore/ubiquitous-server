using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed partial record GameContent
{
    public static CharacterStartingLoadout DevelopmentLoadout { get; } =
        StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Ranger)!;

    public static readonly Ubiquitous.Common.Position PrisonInnerDoor = new(-545.625f, 11.6271f, -2367.96f);

    public const float CosmoDepthPastDoor = 120f;

    public static readonly TimeSpan CosmoArrivesAfter = TimeSpan.FromSeconds(10);

    public static readonly LegendCounterparts PrisonLegend = new(
        Creatures: new Dictionary<uint, uint>
        {
            [2666] = 2669,
            [2668] = 2671,
            [2667] = 2670,
            [2697] = 2698,
            [2672] = 2680,
            [2673] = 2681,
            [2674] = 2682,
            [2675] = 2683,
            [2676] = 2684,
            [2677] = 2685,
            [2678] = 2686,
            [2679] = 2687,
            [2688] = 2689,
            [2690] = 2691,
            [2692] = 2693,
        },
        Spells: new Dictionary<uint, uint>
        {
            [2683] = 2764,
            [2674] = 2761,
            [2675] = 2762,
            [2652] = 2768,
            [2651] = 2766,
            [2796] = 2795,
            [2657] = 2770,
            [2632] = 2771,
            [2633] = 2772,
            [2634] = 2773,
            [2635] = 2774,
            [2645] = 2775,
            [2636] = 2776,
            [2637] = 2777,
            [2638] = 2779,
            [2647] = 2778,
            [2641] = 2722,
            [2646] = 2723,
            [2649] = 2724,
            [2650] = 2725,
            [2653] = 2726,
            [2655] = 2727,
            [2658] = 2728,
            [2659] = 2729,
            [2662] = 2731,
            [2663] = 2732,
            [2664] = 2733,
            [2666] = 2735,
            [2667] = 2736,
            [2669] = 2738,
            [2682] = 2739,
        });

    public static GameContent Development { get; } = new(
        new Dictionary<uint, NpcDefinition>
        {
            [9001] = new(9001, NpcFlags.Vendor, OffersQuests: true),
            [9002] = new(9002, NpcFlags.Teleporter),
            [9003] = new(9003, NpcFlags.Auctioneer),
            [9010] = new(9010, NpcFlags.Gossip, OffersQuests: true),
            [9011] = new(9011, NpcFlags.Gossip, OffersQuests: true),
            [9012] = new(9012, NpcFlags.Gossip, OffersQuests: true),
            [9013] = new(9013, NpcFlags.Gossip, OffersQuests: true),
        },
        new Dictionary<uint, CreatureDefinition>
        {
            [9001] = new(9001, "creature-9001", 3, 100),
            [9010] = new(9010, "creature-9010", 6, 240, MinDamage: 30, MaxDamage: 40,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120,
                RawNpcFlags: (uint)NpcFlags.Gossip),
            [9011] = new(9011, "creature-9011", 10, 380, MinDamage: 40, MaxDamage: 55,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120,
                RawNpcFlags: (uint)NpcFlags.Gossip),
            [9012] = new(9012, "creature-9012", 7, 260, MinDamage: 25, MaxDamage: 33,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120,
                RawNpcFlags: (uint)NpcFlags.Gossip),
            [9013] = new(9013, "creature-9013", 14, 600, MinDamage: 55, MaxDamage: 70,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120,
                RawNpcFlags: (uint)NpcFlags.Gossip),
            [9400] = new(9400, "creature-9400", 10, 320, MinDamage: 20, MaxDamage: 28,
                RespawnTimeMinSeconds: 180, RespawnTimeMaxSeconds: 240),
            [9401] = new(9401, "creature-9401", 14, 4200, MinDamage: 68, MaxDamage: 90,
                RespawnTimeMinSeconds: 900, RespawnTimeMaxSeconds: 1200, Rank: CreatureRank.Elite),
        },
        [
            new QuestDefinition(5001, 9001, 12001, 12002, 1),
            new QuestDefinition(5010, 9010, 12010, 12011, 1),
            new QuestDefinition(5011, 9011, 12012, 12013, 1),
            new QuestDefinition(5012, 9011, 12014, 12015, 1, PrerequisitesAll: [5011]),
            new QuestDefinition(5013, 9012, 12016, 12017, 0, AllowedClassMask: 8),
            new QuestDefinition(5014, 9013, 12018, 12019, 0, PrerequisitesAll: [5013],
                Objectives: [new QuestObjectiveDefinition(
                    0, QuestObjectiveKind.Triggered, 1, ObjectiveStringId: 12020, ObjectiveSpell: 5692)]),
        ],
        new Dictionary<uint, QuestRewardDefinition>
        {
            [5001] = new(5001, 250,
                [new QuestRewardItem(101, 1, 5101, 1)],
                [new QuestRewardItem(102, 1, 5102, 2), new QuestRewardItem(103, 1, 5103, 2)]),
            [5010] = new(5010, 0, [], []),
            [5011] = new(5011, 60, [],
                [new QuestRewardItem(9301, 1, 12031, 2), new QuestRewardItem(9302, 1, 12032, 2)]),
            [5012] = new(5012, 90, [],
                [new QuestRewardItem(9303, 1, 12033, 2), new QuestRewardItem(9304, 1, 12034, 2)]),
            [5013] = new(5013, 40, [], []),
            [5014] = new(5014, 45,
                [new QuestRewardItem(1168, 5, 12035, 2), new QuestRewardItem(9305, 1, 12036, 3)], []),
        },
        new Dictionary<uint, TeleportStopDefinition>
        {
            [1] = new(1, 13, 2f, 2f, 3f),
            [2] = new(2, 13, 200f, 2f, 3f),
            [3] = new(3, 14, 100f, 0f, 100f, 1.5f),
        },
        [
            new TravelRouteDefinition(1, 1, 2, 70),
            new TravelRouteDefinition(2, 2, 3, 430),
        ],
        new Dictionary<uint, IReadOnlyList<VendorTab>>
        {
            [9001] = [new VendorTab(0, 0, 1, true, [new VendorItem(0, 100, 5001, 700, 50, 10, 1)])],
        },
        StartingCurrency: 9_999_999,
        new Dictionary<uint, SpellDefinition>
        {
            [100] = new(100, "spell-100", 1500, 3000, 30f, PowerCost: 10, EffectDamage: 20),
            [OperatorSpells.Finish] = new(
                OperatorSpells.Finish, "operator-finish", 0, 0, 100f,
                PowerCost: 0, EffectDamage: 999_999),
            [OperatorSpells.Restore] = new(
                OperatorSpells.Restore, "operator-restore", 0, 0, 0f,
                PowerCost: 0, EffectHeal: 999_999),
            [23] = new(23, "spell-23", 3500, 1500, 5f),
            [57] = new(57, "spell-57", 2000, 1500, 3.6f),
            [58] = new(58, "spell-58", 0, 3000, 25f),
            [108] = new(108, "spell-108", 0, 3000, 0f),
            [111] = new(111, "spell-111", 0, 0, 60f),
            [648] = new(648, "spell-648", 0, 0, 0f),
            [690] = new(690, "spell-690", 0, 0, 0f),
            [1822] = new(1822, "spell-1822", 2000, 1500, 3.6f),
            [5692] = new(5692, "spell-5692", 500, 3000, 25f, PowerCost: 30,
                EffectRangedWeaponHit: true,
                EffectRangedWeaponDamageBonus: 15,
                EffectRangedWeaponDamagePercent: 100,
                WornSlotNeeded: EquipmentSlots.Ranged,
                RequiredItemClass: ItemClasses.Weapons,
                ItemSubclassesAllowed: (1u << 16) | (1u << 17)),
            [5813] = new(5813, "spell-5813", 0, 25000, 0f, PowerCost: 15, DurationMs: 14000,
                AuraStealth: true),
            [5815] = new(5815, "spell-5815", 0, 1500, 0f, PowerCost: 25, DurationMs: 1800,
                EffectMeleeWeaponHit: true,
                EffectMeleeWeaponSlot: EquipmentSlots.MainHand,
                EffectMeleeWeaponDamageBonus: 10,
                EffectMeleeWeaponDamagePercent: 110),
            [6995] = new(6995, "spell-6995", 0, 0, 0f),
            [20] = new(20, "spell-20", 0, 30000, 0f, EffectHeal: 140, EffectHealRandom: 40),
        },
        StartingSpells: [
            100,
            .. OperatorSpells.All,
            .. DevelopmentLoadout.Spells,
            .. PlaytestSpellbook.RangerToLevel40.Where(spellId => !DevelopmentLoadout.Spells.Contains(spellId)),
        ],
        StartingItems:
        [
            new StartingItemStack(100, 1),
            .. DevelopmentLoadout.BagItems.Select(item => new StartingItemStack(item.ItemEntry, item.Count)),
        ],
        StorageUpgrades:
        [
            new StorageUpgradeDefinition(1, "bag", 18, 12, PriceCurrency: 0, PriceAmount: 100),
            new StorageUpgradeDefinition(2, "bag", 24, 18, PriceCurrency: 0, PriceAmount: 250),
            new StorageUpgradeDefinition(3, "bank", 36, 24, PriceCurrency: 0, PriceAmount: 500),
        ],
        BankBoxEntries: new HashSet<uint> { 9100 },
        new Dictionary<uint, ItemDefinition>
        {
            [100] = new(100, "item-100", Class: 0, SubClass: 0, DisplayInfoId: 100,
                OnUseSpellId: 100, OnUseCooldownSeconds: 3),
            [9301] = new(9301, "item-9301", Class: 4, SubClass: 1, DisplayInfoId: 0,
                BuyPrice: 90, SellPrice: 30, AllowableClassMask: 0, InventoryType: 8, RequiredLevel: 1),
            [9302] = new(9302, "item-9302", Class: 4, SubClass: 2, DisplayInfoId: 0,
                BuyPrice: 110, SellPrice: 36, AllowableClassMask: 10, InventoryType: 7, RequiredLevel: 1),
            [9303] = new(9303, "item-9303", Class: 4, SubClass: 1, DisplayInfoId: 0,
                BuyPrice: 40, SellPrice: 13, AllowableClassMask: 0, InventoryType: 7, RequiredLevel: 1),
            [9304] = new(9304, "item-9304", Class: 4, SubClass: 2, DisplayInfoId: 0,
                BuyPrice: 110, SellPrice: 36, AllowableClassMask: 10, InventoryType: 8, RequiredLevel: 1),
            [1103] = new(1103, "item-1103", Class: 2, SubClass: 0, DisplayInfoId: 91,
                BuyPrice: 102, SellPrice: 34, AllowableClassMask: 10, InventoryType: 11, RequiredLevel: 1),
            [1213] = new(1213, "item-1213", Class: 4, SubClass: 5, DisplayInfoId: 0,
                BuyPrice: 34, SellPrice: 11, AllowableClassMask: 2, InventoryType: 19, RequiredLevel: 1),
            [3070] = new(3070, "item-3070", Class: 4, SubClass: 2, DisplayInfoId: 0,
                BuyPrice: 16, SellPrice: 5, AllowableClassMask: 10, InventoryType: 5, RequiredLevel: 1),
            [3007] = new(3007, "item-3007", Class: 4, SubClass: 2, DisplayInfoId: 0,
                BuyPrice: 16, SellPrice: 5, AllowableClassMask: 10, InventoryType: 6, RequiredLevel: 1),
            [3071] = new(3071, "item-3071", Class: 4, SubClass: 2, DisplayInfoId: 0,
                BuyPrice: 12, SellPrice: 4, AllowableClassMask: 10, InventoryType: 7, RequiredLevel: 1),
            [1168] = new(1168, "item-1168", Class: 0, SubClass: 1, DisplayInfoId: 0,
                OnUseSpellId: 20, OnUseCooldownSeconds: 60, BuyPrice: 150, SellPrice: 1,
                AllowableClassMask: 0),
            [3000] = new(3000, "item-3000", Class: 0, SubClass: 0, DisplayInfoId: 0,
                OnUseSpellId: 22, BuyPrice: 8, SellPrice: 2, AllowableClassMask: 0),
            [3008] = new(3008, "item-3008", Class: 0, SubClass: 1, DisplayInfoId: 0,
                OnUseSpellId: 21, OnUseCooldownSeconds: 60, BuyPrice: 200, SellPrice: 1,
                AllowableClassMask: 20),
            [1053] = new(1053, "item-1053", Class: 4, SubClass: 2, DisplayInfoId: 54,
                BuyPrice: 21, SellPrice: 7, AllowableClassMask: 10, InventoryType: 5, RequiredLevel: 1),
            [1060] = new(1060, "item-1060", Class: 4, SubClass: 2, DisplayInfoId: 61,
                BuyPrice: 21, SellPrice: 7, AllowableClassMask: 10, InventoryType: 6, RequiredLevel: 1),
            [1063] = new(1063, "item-1063", Class: 4, SubClass: 2, DisplayInfoId: 64,
                BuyPrice: 16, SellPrice: 5, AllowableClassMask: 10, InventoryType: 7, RequiredLevel: 1),
            [3907] = new(3907, "item-3907", Class: 2, SubClass: 3, DisplayInfoId: 0,
                BuyPrice: 34, SellPrice: 11, AllowableClassMask: 14, InventoryType: 11),
            [4149] = new(4149, "item-4149", Class: 2, SubClass: 16, DisplayInfoId: 0,
                BuyPrice: 205, SellPrice: 68, AllowableClassMask: 10, InventoryType: 13, RequiredLevel: 1),
            [9305] = new(9305, "item-9305", Class: 0, SubClass: 9, DisplayInfoId: 0,
                BuyPrice: 50, SellPrice: 16, AllowableClassMask: 8),
        },
        MailBoxEntries: new HashSet<uint> { 9102 },
        LootTables: new Dictionary<uint, CreatureLootTableDefinition>
        {
            [9001] = new(9001, [new LootGroupDefinition(1, 1, [new LootTableItemDefinition(100, 100, 1, 1)])]),
        },
        Graveyards: [new MapGraveyardDefinition(1, 13, -130f, 5f, 70f, 0f, ZoneId: 0, Faction: 0),
            new MapGraveyardDefinition(2, 14, -682f, -1.9f, -2098f, 0f, ZoneId: 0, Faction: 0)],
        QuestFinishersByCreature:
        [
            new CreatureQuestBindingDefinition(9011, 5010),
            new CreatureQuestBindingDefinition(9011, 5011),
            new CreatureQuestBindingDefinition(9011, 5012),
            new CreatureQuestBindingDefinition(9013, 5013),
            new CreatureQuestBindingDefinition(9013, 5014),
        ],
        MapGates:
        [
            new MapGateDefinition(
                SourceMapId: 13, SourceMinX: -301, SourceMaxX: -270, SourceMinZ: 362, SourceMaxZ: 386,
                DestinationMapId: 1, DestinationX: -915, DestinationY: 60, DestinationZ: -293, DestinationYaw: 3.14f),
            new MapGateDefinition(
                SourceMapId: 1, SourceMinX: -929, SourceMaxX: -898, SourceMinZ: -334, SourceMaxZ: -302,
                DestinationMapId: 13, DestinationX: -285, DestinationY: 1.5f, DestinationZ: 345, DestinationYaw: 0f),
            new MapGateDefinition(
                SourceMapId: 1, SourceMinX: -2697, SourceMaxX: -2685, SourceMinZ: -597, SourceMaxZ: -581,
                DestinationMapId: 14, DestinationX: -682, DestinationY: -1.9f, DestinationZ: -2098, DestinationYaw: 0f,
                IsDungeonEntrance: true),
            new MapGateDefinition(
                SourceMapId: 14, SourceMinX: -687, SourceMaxX: -680, SourceMinZ: -2094, SourceMaxZ: -2087,
                DestinationMapId: 1, DestinationX: -2678, DestinationY: 39, DestinationZ: -582, DestinationYaw: 2.02f,
                IsDungeonExit: true),
        ],
        Dungeons: [new DungeonDefinition(14, "The Prison", [9401]),
            new DungeonDefinition(14, "The Prison", [2666, 2668, 2667], PrisonLegend)],
        DungeonBarriers:
        [
            new DungeonBarrierDefinition(
                Id: "prison.inner-hall",
                MapId: 14,
                MinX: -548, MaxX: -543, MinZ: -2369, MaxZ: -2364,
                OpenedBy: "boss.2668.down",
                Models: ["dungeon_door01"]),
        ],
        CreatureScripts: new Dictionary<uint, string>
        {
            [2666] = "boss.executioner",
            [2668] = "boss.ariel",
            [2667] = "boss.cosmo",
            [2672] = "inmate.fleshGolem",
            [2673] = "inmate.rottenJailHound",
            [2674] = "inmate.boneGolem",
            [2675] = "inmate.humanPrisoner",
            [2676] = "inmate.viciousOrcPrisoner",
            [2677] = "inmate.rottenFleshGolem",
            [2678] = "inmate.elfPrisoner",
            [2679] = "inmate.viciousHumanPrisoner",
            [2688] = "inmate.viciousElfPrisoner",
            [2690] = "inmate.humanPrisonerCapo",
            [1226] = "creature.trainingDummy",
        },
        DungeonSummons:
        [
            new DungeonSummonDefinition(
                MapId: 14,
                CreatureEntryId: 2667,
                After: ["boss.2668.down", "boss.2666.down"],
                X: PrisonInnerDoor.X, Y: PrisonInnerDoor.Y, Z: PrisonInnerDoor.Z - CosmoDepthPastDoor,
                Notice: "You hear a faraway gate creaking open...",
                Delay: CosmoArrivesAfter),
        ],
        StartingActionBar: [
            .. DevelopmentLoadout.ActionBar,
            new StartingActionButton(2, OperatorSpells.Finish),
            new StartingActionButton(3, OperatorSpells.Restore),
        ]);
}
