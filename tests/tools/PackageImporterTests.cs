using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class PackageImporterTests
{
    [Fact]
    public void ImportBuild114_WritesAValidPackageFromAPackedArchive()
    {
        var archive = BuildArchive();
        var tempDir = Path.Combine(Path.GetTempPath(), "ubiquitous-package-importer-tests-" + Guid.NewGuid());

        try
        {
            var result = PackageImporter.ImportBuild114(
                TableArchiveReader.ReadEntries(archive), tempDir, sourceHash: "test-hash");

            Assert.Equal(1, result.MapCount);
            Assert.Equal(2, result.CreatureCount);
            Assert.Equal(1, result.SpawnCount);
            Assert.Equal(0, result.DroppedSpawnCount);
            Assert.Equal(7, result.SpellCount);
            Assert.Equal(1, result.ItemCount);
            Assert.Equal(1, result.ZoneCount);
            Assert.Equal(0, result.DroppedZoneCount);
            Assert.Equal(2, result.QuestCount);
            Assert.Equal(0, result.DroppedQuestCount);
            Assert.Equal(2, result.NpcCount);
            Assert.Equal(2, result.QuestRewardCount);
            Assert.Equal(2, result.TeleportStopCount);
            Assert.Equal(0, result.DroppedTeleportStopCount);
            Assert.Equal(1, result.MapGraveyardCount);
            Assert.Equal(0, result.DroppedMapGraveyardCount);
            Assert.Equal(1, result.TravelRouteCount);
            Assert.Equal(0, result.DroppedTravelRouteCount);
            Assert.Equal(1, result.StaticObjectCount);
            Assert.Equal(0, result.DroppedStaticObjectCount);
            Assert.Equal(1, result.StorageUpgradeCount);
            Assert.Equal(2, result.StaticObjectTypeCount);
            Assert.Equal(1, result.VendorStockCount);
            Assert.Equal(1, result.StaticObjectQuestStarterCount);
            Assert.Equal(1, result.StaticObjectQuestFinisherCount);
            Assert.Equal(1, result.StaticObjectQuestItemBindingCount);
            Assert.Equal(1, result.CreatureQuestFinisherCount);
            Assert.Equal(1, result.CreatureLootTableCount);
            Assert.Equal(2, result.GossipMenuCount);
            Assert.Equal(1, result.DroppedGossipTeleportCount);
            Assert.Equal(1, result.TalentTreeCount);

            var validation = DataPackageWriter.ValidatePackage(tempDir);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));

            var catalog = DataCatalog.LoadFromDirectory(tempDir);
            Assert.Equal("1.1.4", catalog.Manifest.GameVersion);
            Assert.Equal("test-hash", catalog.Manifest.SourceHash);

            var tree = Assert.Single(catalog.LoadTalentTrees());
            Assert.Equal(11u, tree.Id);
            Assert.Equal(1, tree.ClassId);
            Assert.Equal([1u, 2u], tree.Talents.Select(talent => talent.Id));
            var second = tree.Talents[1];
            Assert.Equal(5, second.PointsInTreeRequired);
            Assert.Equal([1u], second.Prerequisites);
            Assert.Equal([90u, 91u], second.RankSpellIds);

            var creature = Assert.Single(catalog.LoadCreatures(), c => c.EntryId == 12u);
            Assert.Equal("Wolf", creature.NameKey);

            var map = Assert.Single(catalog.LoadMaps());
            Assert.Equal("Starter Map", map.InternalName);

            var graveyard = Assert.Single(catalog.LoadMapGraveyards());
            Assert.Equal((ushort)13, graveyard.MapId);
            Assert.Equal(-111.4f, graveyard.X);

            var spells = catalog.LoadSpells();
            Assert.Equal(7, spells.Count);
            var spell = Assert.Single(spells, s => s.Id == 85u);
            Assert.Equal("test-bolt", spell.NameKey);
            Assert.Equal(15, spell.PowerCost);
            Assert.Equal(20, spell.EffectDamage);
            Assert.Equal(10, spell.EffectDamageRandom);
            Assert.Equal([4u, 0u, 0u], spell.SpellGroupMask);
            Assert.Equal(50, spell.AuraSlowPercent);

            var rapidShot = Assert.Single(spells, s => s.Id == 90u);
            Assert.Equal("test-ranged-attack", rapidShot.NameKey);
            Assert.Equal(30, rapidShot.PowerCost);
            Assert.Null(rapidShot.EffectDamage);
            Assert.Null(rapidShot.EffectHeal);
            Assert.True(rapidShot.EffectRangedWeaponHit);
            Assert.Equal(15, rapidShot.EffectRangedWeaponDamageBonus);
            Assert.Equal(100, rapidShot.EffectRangedWeaponDamagePercent);
            Assert.Equal(999u, rapidShot.EffectTriggerSpellId);
            Assert.Equal(100, rapidShot.EffectTriggerChancePercent);
            Assert.False(rapidShot.EffectMeleeWeaponHit);

            var shadowStrike = Assert.Single(spells, s => s.Id == 91u);
            Assert.Equal("test-melee-strike", shadowStrike.NameKey);
            Assert.Equal(25, shadowStrike.PowerCost);
            Assert.True(shadowStrike.EffectMeleeWeaponHit);
            Assert.Equal(EquipmentSlots.MainHand, shadowStrike.EffectMeleeWeaponSlot);
            Assert.Equal(10, shadowStrike.EffectMeleeWeaponDamageBonus);
            Assert.Equal(110, shadowStrike.EffectMeleeWeaponDamagePercent);
            Assert.Equal(999u, shadowStrike.EffectTriggerSpellId);
            Assert.Null(shadowStrike.EffectDamage);
            Assert.False(shadowStrike.AuraStealth);

            var sneak = Assert.Single(spells, s => s.Id == 92u);
            Assert.Equal("test-stealth", sneak.NameKey);
            Assert.Equal(15, sneak.PowerCost);
            Assert.True(sneak.AuraStealth);
            Assert.Equal(14000, sneak.DurationMs);
            Assert.False(sneak.EffectMeleeWeaponHit);
            Assert.Null(sneak.EffectDamage);

            var offhand = Assert.Single(spells, s => s.Id == 93u);
            Assert.True(offhand.EffectMeleeWeaponHit);
            Assert.Equal(EquipmentSlots.OffHand, offhand.EffectMeleeWeaponSlot);
            Assert.Equal(150, offhand.EffectMeleeWeaponDamagePercent);

            var shield = Assert.Single(spells, s => s.Id == 99u);
            Assert.Equal(50, shield.AuraAbsorbAmount);
            Assert.Equal(100, shield.AuraAbsorbPercent);
            Assert.Null(shield.EffectHeal);
            Assert.Null(offhand.AuraAbsorbAmount);

            var layered = Assert.Single(spells, s => s.Id == 80u);
            Assert.Equal(20, layered.EffectDamage);
            Assert.Collection(layered.ExtraEffects!,
                dot =>
                {
                    Assert.Equal(2, dot.AuraPeriodicDamage);
                    Assert.Equal(SpellEffectTargets.SingleEnemy, dot.EffectTarget);
                    Assert.Equal(6000, dot.DurationMs);
                    Assert.Null(dot.EffectDamage);
                },
                buff =>
                {
                    Assert.Equal(3, buff.AuraAttributeId);
                    Assert.Equal(5, buff.AuraAttributeAmount);
                    Assert.Equal(SpellEffectTargets.Self, buff.EffectTarget);
                    Assert.Null(buff.ExtraEffects);
                });
            Assert.Null(layered.AuraSlowPercent);
            Assert.Null(spell.ExtraEffects);

            var item = Assert.Single(catalog.LoadItems());
            Assert.Equal("Shield", item.NameKey);
            Assert.Equal((byte)4, item.Class);

            Assert.False(catalog.Manifest.Datasets.ContainsKey("zones"));

            var quests = catalog.LoadQuests();
            Assert.Equal(2, quests.Count);
            var quest = Assert.Single(quests, q => q.Id == 5000u);
            Assert.Equal(12u, quest.GiverEntryId);
            Assert.Equal(1, quest.MinLevel);

            var staticObjectQuest = Assert.Single(quests, q => q.Id == 5002u);
            Assert.Null(staticObjectQuest.GiverEntryId);

            var starter = Assert.Single(catalog.LoadStaticObjectQuestStarters());
            Assert.Equal(2u, starter.StaticObjectEntry);
            Assert.Equal(5002u, starter.QuestId);
            var finisher = Assert.Single(catalog.LoadStaticObjectQuestFinishers());
            Assert.Equal(2u, finisher.StaticObjectEntry);
            Assert.Equal(5002u, finisher.QuestId);

            var npcs = catalog.LoadNpcs();
            Assert.Equal(2, npcs.Count);
            var npc = Assert.Single(npcs, n => n.EntryId == 12u);
            Assert.True(npc.Flags.HasFlag(NpcFlags.Vendor));
            Assert.True(npc.OffersQuests);
            var gossipOnlyNpc = Assert.Single(npcs, n => n.EntryId == 13u);
            Assert.False(gossipOnlyNpc.Flags.HasFlag(NpcFlags.Vendor));
            Assert.Equal(60u, gossipOnlyNpc.GreetingMenuId);
            Assert.Equal(0u, npc.GreetingMenuId);

            var gossipMenus = catalog.LoadGossipMenus();
            var greeting = Assert.Single(gossipMenus, m => m.Id == 60u);
            Assert.Equal([61u], greeting.SubMenus);
            Assert.Empty(greeting.Teleports);
            Assert.Null(greeting.Scripts);
            var travel = Assert.Single(gossipMenus, m => m.Id == 61u);
            Assert.Empty(travel.SubMenus);
            Assert.Equal([7u, 8u, 0u, 0u, 0u, 0u], travel.Scripts);
            Assert.Equal(
                new GossipTeleportDefinition(1, 900, 13, -10f, 2.5f, 40f, 1.5f, 0, 250),
                Assert.Single(travel.Teleports));

            var stock = Assert.Single(catalog.LoadVendorStock());
            Assert.Equal(12u, stock.SellerEntryId);
            var stockItem = Assert.Single(stock.Tab.Items);
            Assert.Equal(200, stockItem.Entry);
            Assert.Equal(75, stockItem.BuyPrice);
            Assert.Equal(5, stockItem.MaxCount);
            Assert.Equal(1, stockItem.StackCount);

            var rewards = catalog.LoadQuestRewards();
            Assert.Equal(2, rewards.Count);
            var reward = Assert.Single(rewards, r => r.QuestId == 5000u);
            Assert.Equal(50, reward.Money);
            var rewardItem = Assert.Single(reward.Fixed);
            Assert.Equal(200u, rewardItem.Entry);
            Assert.Equal(2, rewardItem.Count);
            Assert.Equal(950u, rewardItem.NameTextId);
            Assert.Equal(3, rewardItem.Quality);

            var stops = catalog.LoadTeleportStops();
            Assert.Equal(2, stops.Count);
            Assert.Contains(stops, s => s.Id == 30u && s.MapId == 13);

            var route = Assert.Single(catalog.LoadTravelRoutes());
            Assert.Equal(30u, route.FirstStop);
            Assert.Equal(31u, route.SecondStop);
            Assert.Equal(45, route.Price);

            var staticObject = Assert.Single(catalog.LoadStaticObjectSpawns());
            Assert.Equal(700u, staticObject.Id);
            Assert.Equal((ushort)13, staticObject.MapId);

            var upgrade = Assert.Single(catalog.LoadStorageUpgrades());
            Assert.Equal("bag", upgrade.Area);
            Assert.Equal(18u, upgrade.SlotCount);
            Assert.Equal(12u, upgrade.FromSlotCount);

            var staticTypes = catalog.LoadStaticObjectTypes();
            Assert.Equal(2, staticTypes.Count);
            var staticType = Assert.Single(staticTypes, t => t.Entry == 1u);
            Assert.Equal(StaticObjectTypes.StorageBox, staticType.Type);

            var lootTable = Assert.Single(catalog.LoadCreatureLootTables());
            Assert.Equal(12u, lootTable.CreatureEntryId);
            var lootGroup = Assert.Single(lootTable.Groups);
            Assert.Equal(1, lootGroup.GuaranteedDrops);
            var lootItem = Assert.Single(lootGroup.Items);
            Assert.Equal(200u, lootItem.ItemId);
            Assert.Equal(100, lootItem.DropChancePercent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportBuild114_FailsLoudlyWhenARequiredTableIsMissing()
    {
        var archive = BuildArchive(omit: "tables/spell_new.tbl");

        Assert.Throws<FTableFormatException>(() => PackageImporter.ImportBuild114(
            TableArchiveReader.ReadEntries(archive), Path.GetTempPath(), sourceHash: "test-hash"));
    }

    private static byte[] BuildArchive(string? omit = null)
    {
        var mapInfo = BuildTable(new string('u', 9) + "s",
            new Dictionary<int, object> { [0] = 13u, [9] = "Starter Map" });

        var creatureProto = BuildTable(new string('u', 55) + "su",
        [
            new Dictionary<int, object>
            {
                [0] = 12u, [4] = 3u, [8] = 80u, [11] = (uint)Ubiquitous.Data.NpcFlags.Vendor, [12] = 2000u,
                [14] = 8u, [15] = 16u, [19] = 60u, [20] = 120u, [53] = 1u, [54] = 5u,
                [55] = "1,",
            },
            new Dictionary<int, object>
            {
                [0] = 13u, [4] = 3u, [8] = 80u, [11] = (uint)Ubiquitous.Data.NpcFlags.Gossip, [12] = 2000u,
                [14] = 8u, [15] = 16u, [19] = 60u, [20] = 120u, [53] = 1u, [54] = 5u,
                [55] = "1",
                [56] = 60u,
            },
        ]);
        var sellProgram = BuildTable("usu", new Dictionary<int, object> { [0] = 1u, [1] = "test program", [2] = 0u });
        var sellProgramItem = BuildTable("uuuuuuuuuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 1u, [2] = 200u, [3] = 1u, [4] = 5u });

        var creatureAppearance = BuildTable("uu",
            [
                new Dictionary<int, object> { [0] = 12u, [1] = 900u },
                new Dictionary<int, object> { [0] = 13u, [1] = 901u },
            ]);
        var stringCreature = BuildTable("us",
            [
                new Dictionary<int, object> { [0] = 900u, [1] = "Wolf" },
                new Dictionary<int, object> { [0] = 901u, [1] = "Gossiping Wolf" },
            ]);

        var creatureSpawns = BuildTable("uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
            new Dictionary<int, object>
            {
                [0] = 500u,
                [1] = 12u,
                [2] = 13u,
                [3] = 1f,
                [4] = 2f,
                [5] = 3f,
                [6] = 0f,
                [7] = (sbyte)1,
                [26] = 0u,
                [27] = 4f,
            });

        var spellFormat = new string('u', 47).ToCharArray();
        spellFormat[21] = 'f';
        spellFormat[39] = 'f';
        var spellNew = BuildTable(new string(spellFormat),
        [
            new Dictionary<int, object>
            {
                [0] = 85u, [5] = 700u, [16] = 1000u, [17] = 2000u, [21] = 30f, [33] = 500u, [34] = 511u, [41] = 15u,
                [44] = 4u, [45] = 0u, [46] = 0u,
            },
            new Dictionary<int, object>
            {
                [0] = 90u, [5] = 701u, [16] = 500u, [17] = 3000u, [21] = 25f, [33] = 501u, [34] = 502u,
                [35] = 502u, [41] = 30u,
            },
            new Dictionary<int, object>
            {
                [0] = 91u, [5] = 702u, [16] = 0u, [17] = 1500u, [21] = 0f, [31] = 1800u,
                [33] = 503u, [34] = 502u, [41] = 25u,
            },
            new Dictionary<int, object>
            {
                [0] = 92u, [5] = 703u, [16] = 0u, [17] = 25000u, [21] = 0f, [31] = 14000u,
                [33] = 504u, [41] = 15u,
            },
            new Dictionary<int, object>
            {
                [0] = 93u, [5] = 704u, [16] = 0u, [17] = 1500u, [21] = 0f, [33] = 505u, [41] = 25u,
            },
            new Dictionary<int, object>
            {
                [0] = 99u, [5] = 704u, [16] = 0u, [17] = 30000u, [21] = 0f, [31] = 300000u, [33] = 510u, [41] = 30u,
            },
            new Dictionary<int, object>
            {
                [0] = 80u, [5] = 700u, [16] = 2000u, [17] = 1000u, [21] = 25f, [31] = 6000u,
                [33] = 500u, [34] = 520u, [35] = 530u, [36] = 540u, [37] = 550u, [41] = 35u,
            },
        ]);
        var stringSpell = BuildTable("us",
        [
            new Dictionary<int, object> { [0] = 700u, [1] = "test-bolt" },
            new Dictionary<int, object> { [0] = 701u, [1] = "test-ranged-attack" },
            new Dictionary<int, object> { [0] = 702u, [1] = "test-melee-strike" },
            new Dictionary<int, object> { [0] = 703u, [1] = "test-stealth" },
            new Dictionary<int, object> { [0] = 704u, [1] = "Test Strike" },
        ]);

        var effectFormat = "uuuuufuuiiiuuuuuuuuuusss";
        var spellEffect = BuildTable(effectFormat,
        [
            new Dictionary<int, object> { [0] = 500u, [6] = 0u, [7] = 1u, [8] = 1, [9] = 20, [10] = 10 },
            new Dictionary<int, object> { [0] = 501u, [6] = 0u, [7] = 28u, [8] = 7, [9] = 15, [11] = 100u },
            new Dictionary<int, object> { [0] = 502u, [6] = 0u, [7] = 9u, [9] = 100, [11] = 999u },
            new Dictionary<int, object> { [0] = 503u, [6] = 0u, [7] = 23u, [8] = 1, [9] = 10, [11] = 110u },
            new Dictionary<int, object> { [0] = 504u, [6] = 1u, [7] = 23u },
            new Dictionary<int, object> { [0] = 505u, [6] = 0u, [7] = 27u, [9] = 0, [11] = 150u },
            new Dictionary<int, object> { [0] = 510u, [6] = 1u, [7] = 3u, [9] = 50, [11] = 100u },
            new Dictionary<int, object> { [0] = 511u, [6] = 1u, [7] = 7u, [9] = 50 },
            new Dictionary<int, object> { [0] = 520u, [4] = 2u, [6] = 1u, [7] = 1u, [9] = 2 },
            new Dictionary<int, object> { [0] = 530u, [4] = 2u, [6] = 1u, [7] = 12u },
            new Dictionary<int, object> { [0] = 540u, [4] = 2u, [6] = 1u, [7] = 7u, [9] = 50, [16] = 4u },
            new Dictionary<int, object> { [0] = 550u, [4] = 1u, [6] = 1u, [7] = 4u, [9] = 5, [11] = 3u },
        ]);

        var items = BuildTable(new string('u', 62),
            new Dictionary<int, object> { [0] = 200u, [1] = 4u, [2] = 5u, [3] = 950u, [4] = 200u, [5] = 3u, [8] = 75u });
        var stringItem = BuildTable("us", new Dictionary<int, object> { [0] = 950u, [1] = "Shield" });

        var zones = BuildTable("iiiufffffff",
            new Dictionary<int, object> { [0] = 900u, [3] = 13u, [4] = 0f, [6] = 0f, [8] = 100f, [10] = 100f });
        var stringZone = BuildTable("us", new Dictionary<int, object> { [0] = 900u, [1] = "Starter Zone" });

        var quests = BuildTable(new string('u', 126),
        [
            new Dictionary<int, object>
            {
                [0] = 5000u, [4] = 1u, [24] = 155u, [25] = 156u, [87] = 200u, [91] = 2u, [108] = 50u,
            },
            new Dictionary<int, object> { [0] = 5002u, [4] = 1u, [24] = 12007u, [25] = 12008u },
        ]);
        var questStarter = BuildTable("uu", new Dictionary<int, object> { [0] = 12u, [1] = 5000u });
        var creatureQuestFinisher = BuildTable("uu", new Dictionary<int, object> { [0] = 12u, [1] = 5000u });

        var mapGraveyard = BuildTable("uffffuuu",
            new Dictionary<int, object> { [0] = 40u, [1] = -111.4f, [2] = -5f, [3] = 53.9f, [4] = 0f, [5] = 0u, [6] = 13u, [7] = 0u });

        var teleportSpot = BuildTable("usuffffu",
        [
            new Dictionary<int, object> { [0] = 30u, [2] = 13u, [3] = -507f, [4] = 5.8f, [5] = 147.4f, [6] = 1.57f, [7] = 0u },
            new Dictionary<int, object> { [0] = 31u, [2] = 13u, [3] = 1127f, [4] = 0f, [5] = 514f, [6] = 0f, [7] = 1u },
        ]);
        var busRoute = BuildTable("uuuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 30u, [2] = 31u, [3] = 45u, [4] = 7559u });
        var staticObjectSpawns = BuildTable("uuuffffffuufffuf",
            new Dictionary<int, object> { [0] = 700u, [1] = 1u, [2] = 13u, [3] = -453f, [4] = 40.6f, [5] = 203.4f, [7] = 3.14159f });
        var storageUpgrade = BuildTable("uuuuuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 0u, [2] = 18u, [3] = 0u, [4] = 100u, [5] = 0u, [6] = 12u });
        var staticObjectProto = BuildTable("uuuuuuufuuuuuuuuuuuu",
        [
            new Dictionary<int, object> { [0] = 1u, [1] = 9u },
            new Dictionary<int, object> { [0] = 2u, [1] = 5u },
        ]);
        var staticObjectQuestStarter = BuildTable("uu", new Dictionary<int, object> { [0] = 2u, [1] = 5002u });
        var staticObjectQuestFinisher = BuildTable("uu", new Dictionary<int, object> { [0] = 2u, [1] = 5002u });
        var staticObjectQuestItemBinding = BuildTable("uuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 5000u, [2] = 200u, [3] = 1u });

        var loot = BuildTable("uuuu", new Dictionary<int, object> { [0] = 1u, [1] = 12u, [2] = 1u, [3] = 1u });
        var lootFeature = BuildTable("uuuuuuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 0u, [2] = 1u, [3] = 10u, [4] = 0u, [5] = 0u, [6] = 1u, [7] = 1u });
        var lootFeatureItems = BuildTable("uuufuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 1u, [2] = 200u, [3] = 100f, [4] = 1u, [5] = 1u });

        var menuRows = BuildTable(new string('u', 14),
        [
            new Dictionary<int, object> { [0] = 60u, [1] = 800u, [8] = 61u },
            new Dictionary<int, object> { [0] = 61u, [1] = 820u, [2] = 7u, [3] = 8u },
        ]);
        var stringMenu = BuildTable("us",
        [
            new Dictionary<int, object>
            {
                [0] = 800u,
                [1] = "Hello.\u0012link_menu=1,icon=a.png\u0013Travel\u0012/link_menu\u0013"
                    + "\u0012link_menu=-1,icon=a.png\u0013Bye\u0012/link_menu\u0013",
            },
            new Dictionary<int, object>
            {
                [0] = 820u,
                [1] = "\u0012teleport=1,topzone=900,x=-10,y=2.5,z=40,o=1.5,moneytype=0,moneycount=250\u0013\u0012/teleport\u0013"
                    + "\u0012teleport=2,topzone=999,x=1,y=2,z=3,o=0\u0013\u0012/teleport\u0013"
                    + "\u0012link_menu=-1,icon=a.png,script=1\u0013Here\u0012/link_menu\u0013"
                    + "\u0012link_menu=-1,icon=a.png,script=2\u0013There\u0012/link_menu\u0013",
            },
        ]);

        var characterClass = BuildTable("uuuuuuuu",
            new Dictionary<int, object> { [0] = 1u, [1] = 2u, [3] = 11u });
        var spellTalent = BuildTable("uuuuuuuuuuuuuusu",
        [
            new Dictionary<int, object> { [0] = 1u, [1] = 1u, [2] = 1u, [3] = 11u, [8] = 1u, [9] = 85u, [14] = "icon_a.png", [15] = 10u },
            new Dictionary<int, object> { [0] = 2u, [1] = 2u, [2] = 1u, [3] = 11u, [4] = 5u, [5] = 1u, [8] = 2u, [9] = 90u, [10] = 91u, [14] = "icon_b.png", [15] = 12u },
            new Dictionary<int, object> { [0] = 3u, [1] = 1u, [2] = 1u, [3] = 99u, [8] = 1u, [9] = 85u, [14] = "", [15] = 0u },
        ]);

        var files = new Dictionary<string, byte[]>
        {
            ["tables/character_class.tbl"] = characterClass,
            ["tables/spell_talent.tbl"] = spellTalent,
            ["tables/chat_menu.tbl"] = menuRows,
            ["tables/String_Menu_en.tbl"] = stringMenu,
            ["tables/map_info.tbl"] = mapInfo,
            ["tables/creature_proto.tbl"] = creatureProto,
            ["tables/creature_appearance.tbl"] = creatureAppearance,
            ["tables/String_Creature_en.tbl"] = stringCreature,
            ["tables/creature_spawns.tbl"] = creatureSpawns,
            ["tables/spell_new.tbl"] = spellNew,
            ["tables/String_Spell_en.tbl"] = stringSpell,
            ["tables/spell_effect.tbl"] = spellEffect,
            ["tables/items.tbl"] = items,
            ["tables/String_Item_en.tbl"] = stringItem,
            ["tables/zones.tbl"] = zones,
            ["tables/String_Zones_en.tbl"] = stringZone,
            ["tables/quests.tbl"] = quests,
            ["tables/creature_quest_starter.tbl"] = questStarter,
            ["tables/creature_quest_finisher.tbl"] = creatureQuestFinisher,
            ["tables/creature_machant_sell_program.tbl"] = sellProgram,
            ["tables/creature_machant_sell_program_item.tbl"] = sellProgramItem,
            ["tables/map_graveyard.tbl"] = mapGraveyard,
            ["tables/teleport_spot.tbl"] = teleportSpot,
            ["tables/t_bus_route.tbl"] = busRoute,
            ["tables/staticobject_spawns.tbl"] = staticObjectSpawns,
            ["tables/storage_upgrade.tbl"] = storageUpgrade,
            ["tables/staticobject_proto.tbl"] = staticObjectProto,
            ["tables/staticobject_quest_starter.tbl"] = staticObjectQuestStarter,
            ["tables/staticobject_quest_finisher.tbl"] = staticObjectQuestFinisher,
            ["tables/staticobject_quest_item_binding.tbl"] = staticObjectQuestItemBinding,
            ["tables/loot.tbl"] = loot,
            ["tables/loot_feature.tbl"] = lootFeature,
            ["tables/loot_feature_items.tbl"] = lootFeatureItems,
        };
        if (omit is not null)
        {
            files.Remove(omit);
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(contents);
            }
        }

        byte[] bytes = stream.ToArray();
        byte[] standardMagic = [0x50, 0x4B, 0x03, 0x04];
        byte[] packedMagic = "GBMP"u8.ToArray();
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            if (bytes.AsSpan(i, 4).SequenceEqual(standardMagic))
            {
                packedMagic.CopyTo(bytes, i);
            }
        }

        return bytes;
    }

    private static byte[] BuildTable(string format, Dictionary<int, object> row) => BuildTable(format, [row]);

    private static byte[] BuildTable(string format, IReadOnlyList<Dictionary<int, object>> rows)
    {
        int cols = format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = format[i] switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 };
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        int rowCount = rows.Count;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rowCount * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize * rowCount;

        var pool = new List<byte>();
        var stringOffsets = new Dictionary<(int row, int col), (int offset, int length)>();
        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (format[c] == 's' && rows[r].TryGetValue(c, out var v) && v is string s)
                {
                    var bytes = Encoding.UTF8.GetBytes(s + "\0");
                    stringOffsets[(r, c)] = (offsetStrings + pool.Count, bytes.Length);
                    pool.AddRange(bytes);
                }
            }
        }

        var data = new byte[offsetStrings + pool.Count];
        pool.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rowCount);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, pool.Count);
        WriteAscii(data, offsetFormat, format);

        int at = offsetEntry;
        for (int r = 0; r < rowCount; r++)
        {
            var row = rows[r];
            for (int c = 0; c < cols; c++)
            {
                if (format[c] == 's')
                {
                    var (offset, length) = stringOffsets.TryGetValue((r, c), out var v) ? v : (0, 0);
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)offset);
                    BitConverter.GetBytes((short)length).CopyTo(data, at + 4);
                }
                else if (format[c] == 'f')
                {
                    float value = row.TryGetValue(c, out var v) ? (float)v : 0f;
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), value);
                }
                else if (format[c] == 'c')
                {
                    data[at] = row.TryGetValue(c, out var v) ? unchecked((byte)Convert.ToSByte(v)) : (byte)0;
                }
                else if (format[c] == 'h')
                {
                    short value = row.TryGetValue(c, out var v) ? Convert.ToInt16(v) : (short)0;
                    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at, 2), value);
                }
                else
                {
                    uint value = row.TryGetValue(c, out var v) ? Convert.ToUInt32(v) : 0u;
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
                }

                at += widths[c];
            }
        }

        return data;
    }

    private static void WriteU32(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
    }

    private static void WriteAscii(byte[] data, int at, string text)
    {
        for (int i = 0; i < text.Length && at + i < data.Length; i++)
        {
            data[at + i] = (byte)text[i];
        }
    }
}
