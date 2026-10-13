using System.Text.Json;

namespace Ubiquitous.Data.Tests;

public sealed class ContentDatasetTests
{
    [Fact]
    public void Catalog_LoadsNpcOverlaysForKnownCreatures()
    {
        Package(dir =>
        {
            var npc = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadNpcs());

            Assert.Equal((uint)12, npc.EntryId);
            Assert.True(npc.Flags.HasFlag(NpcFlags.Teleporter));
        });
    }

    [Fact]
    public void Catalog_ReadsEachDatasetFromDiskOnce()
    {
        Package(dir =>
        {
            var catalog = DataCatalog.LoadFromDirectory(dir);
            var first = catalog.LoadCreatures();
            File.Delete(Path.Combine(dir, "creatures.json"));

            Assert.Same(first, catalog.LoadCreatures());
            Assert.Single(catalog.LoadNpcs());
        });
    }

    [Fact]
    public void Catalog_RejectsAnNpcOverlayOnNoCreature()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadNpcs());

            Assert.Contains("missing creature 77", error.Message);
        }, npcs: [new NpcDefinition(77, NpcFlags.Vendor)]);
    }

    [Fact]
    public void Catalog_LoadsQuestsHeldByAKnownNpc()
    {
        Package(dir =>
        {
            var quest = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Equal((uint)5001, quest.Id);
            Assert.Equal((uint)12, quest.GiverEntryId);
        });
    }

    [Fact]
    public void Catalog_RejectsAQuestHeldByNobody()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Contains("missing NPC 88", error.Message);
        }, quests: [new QuestDefinition(5001, 88, 1, 2, 1)]);
    }

    [Fact]
    public void Catalog_LoadsAKillObjectiveNamingAKnownCreature()
    {
        Package(dir =>
        {
            var quest = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadQuests());

            var objective = Assert.Single(quest.Objectives);
            Assert.Equal(QuestObjectiveKind.Killed, objective.Kind);
            Assert.Equal((uint)12, objective.ObjectiveCreature);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 3, ObjectiveCreature: 12)]),
        ]);
    }

    [Fact]
    public void Catalog_RejectsAKillObjectiveNamingAMissingCreature()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Contains("missing creature 999", error.Message);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 3, ObjectiveCreature: 999)]),
        ]);
    }

    [Fact]
    public void Catalog_RejectsAnObjectiveRequiringNoCount()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Contains("not positive", error.Message);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 0, ObjectiveCreature: 12)]),
        ]);
    }

    [Fact]
    public void Catalog_RejectsAHeldObjectiveNamingNoItem()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Contains("held on no item", error.Message);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 0)]),
        ]);
    }

    [Fact]
    public void Catalog_RejectsAHeldObjectiveNamingAMissingItem()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Contains("missing item 999", error.Message);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 999)]),
        ], items: [new ItemDefinition(101, "item-101", Class: 0, SubClass: 0, DisplayInfoId: 0)]);
    }

    [Fact]
    public void Catalog_LoadsAHeldObjectiveNamingAKnownItem()
    {
        Package(dir =>
        {
            var quest = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadQuests());

            Assert.Equal(101u, Assert.Single(quest.Objectives).ObjectiveItem);
        }, quests:
        [
            new QuestDefinition(5001, 12, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 101)]),
        ], items: [new ItemDefinition(101, "item-101", Class: 0, SubClass: 0, DisplayInfoId: 0)]);
    }

    [Fact]
    public void Catalog_LoadsQuestRewardsForKnownQuests()
    {
        Package(dir =>
        {
            var reward = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadQuestRewards());

            Assert.Equal(250, reward.Money);
            Assert.Equal((uint)101, Assert.Single(reward.Fixed).Entry);
        });
    }

    [Fact]
    public void Catalog_RejectsARewardForAQuestThatDoesNotExist()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuestRewards());

            Assert.Contains("missing quest 9999", error.Message);
        }, rewards: [new QuestRewardDefinition(9999, 10, [], [])]);
    }

    [Fact]
    public void Catalog_RejectsARewardPayingAnItemNoTimes()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadQuestRewards());

            Assert.Contains("paying an item no times", error.Message);
        }, rewards: [new QuestRewardDefinition(5001, 10, [new QuestRewardItem(101, 0, 1, 1)], [])]);
    }

    [Fact]
    public void Catalog_LoadsTravelStopsOnKnownMaps()
    {
        Package(dir =>
        {
            var stops = DataCatalog.LoadFromDirectory(dir).LoadTeleportStops();

            Assert.Equal(2, stops.Count);
            Assert.Equal((uint)1, stops[0].Id);
            Assert.Equal((ushort)7, stops[0].MapId);
            Assert.Equal(TeleportStopKind.BusStop, stops[0].Kind);
        });
    }

    [Fact]
    public void Catalog_LoadsADungeonGateStopByItsKind()
    {
        Package(dir =>
        {
            var stops = DataCatalog.LoadFromDirectory(dir).LoadTeleportStops();

            Assert.Equal(TeleportStopKind.DungeonGate,
                stops.Single(stop => stop.Id == 2).Kind);
        }, stops:
        [
            new TeleportStopDefinition(1, 7, 1, 2, 3),
            new TeleportStopDefinition(2, 7, 9, 2, 3, Kind: TeleportStopKind.DungeonGate),
        ]);
    }

    [Fact]
    public void Catalog_RejectsATravelStopOnNoMap()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadTeleportStops());

            Assert.Contains("missing map 99", error.Message);
        }, stops: [new TeleportStopDefinition(1, 99, 0, 0, 0)]);
    }

    [Fact]
    public void Catalog_LoadsTravelRoutesJoiningKnownStops()
    {
        Package(dir =>
        {
            var route = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadTravelRoutes());

            Assert.Equal(70, route.Price);
        }, stops: [new TeleportStopDefinition(1, 7, 1, 2, 3), new TeleportStopDefinition(2, 7, 9, 2, 3)]);
    }

    [Fact]
    public void Catalog_RejectsARouteToAStopThatDoesNotExist()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadTravelRoutes());

            Assert.Contains("missing stop", error.Message);
        }, routes: [new TravelRouteDefinition(1, 1, 42, 70)]);
    }

    [Fact]
    public void Catalog_LoadsMapGraveyardsOnKnownMaps()
    {
        Package(dir =>
        {
            var graveyards = DataCatalog.LoadFromDirectory(dir).LoadMapGraveyards();

            var graveyard = Assert.Single(graveyards);
            Assert.Equal((uint)1, graveyard.Id);
            Assert.Equal((ushort)7, graveyard.MapId);
            Assert.Equal(1f, graveyard.X);
            Assert.Equal(2f, graveyard.Y);
            Assert.Equal(3f, graveyard.Z);
        });
    }

    [Fact]
    public void Catalog_RejectsAMapGraveyardOnNoMap()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadMapGraveyards());

            Assert.Contains("missing map 99", error.Message);
        }, graveyards: [new MapGraveyardDefinition(1, 99, 0, 0, 0, 0, ZoneId: 0, Faction: 0)]);
    }

    [Fact]
    public void Catalog_RejectsARouteJoiningAStopToItself()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadTravelRoutes());

            Assert.Contains("joining a stop to itself", error.Message);
        }, routes: [new TravelRouteDefinition(1, 1, 1, 70)]);
    }

    [Fact]
    public void Catalog_LoadsAStorageUpgradeRung()
    {
        Package(dir =>
        {
            var upgrade = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadStorageUpgrades());

            Assert.Equal("bag", upgrade.Area);
            Assert.Equal((uint)18, upgrade.SlotCount);
            Assert.Equal((uint)12, upgrade.FromSlotCount);
        });
    }

    [Fact]
    public void Catalog_RejectsAStorageUpgradeWithAnUnrecognizedArea()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadStorageUpgrades());

            Assert.Contains("unrecognized area", error.Message);
        }, storageUpgrades: [new StorageUpgradeDefinition(1, "vault", 18, 12, 0, 100)]);
    }

    [Fact]
    public void Catalog_RejectsAStorageUpgradeThatDoesNotRaiseCapacity()
    {
        Package(dir =>
        {
            var error = Assert.Throws<InvalidDataException>(
                () => DataCatalog.LoadFromDirectory(dir).LoadStorageUpgrades());

            Assert.Contains("does not raise capacity", error.Message);
        }, storageUpgrades: [new StorageUpgradeDefinition(1, "bag", 12, 12, 0, 100)]);
    }

    [Fact]
    public void Catalog_LoadsAStaticObjectType()
    {
        Package(dir =>
        {
            var type = Assert.Single(DataCatalog.LoadFromDirectory(dir).LoadStaticObjectTypes());

            Assert.Equal(145u, type.Entry);
            Assert.Equal(StaticObjectTypes.StorageBox, type.Type);
        });
    }

    private static void Package(
        Action<string> assert,
        IReadOnlyList<NpcDefinition>? npcs = null,
        IReadOnlyList<QuestDefinition>? quests = null,
        IReadOnlyList<QuestRewardDefinition>? rewards = null,
        IReadOnlyList<TeleportStopDefinition>? stops = null,
        IReadOnlyList<TravelRouteDefinition>? routes = null,
        IReadOnlyList<StorageUpgradeDefinition>? storageUpgrades = null,
        IReadOnlyList<StaticObjectTypeDefinition>? staticObjectTypes = null,
        IReadOnlyList<MapGraveyardDefinition>? graveyards = null,
        IReadOnlyList<ItemDefinition>? items = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            DataManifestJson.Save(
                new DataManifest("1.0.0", "1.1.4", "abc123",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new Dictionary<string, string>
                    {
                        ["maps"] = "maps.json",
                        ["spells"] = "spells.json",
                        ["creatures"] = "creatures.json",
                        ["npcs"] = "npcs.json",
                        ["quests"] = "quests.json",
                        ["questRewards"] = "quest-rewards.json",
                        ["teleportStops"] = "teleport-stops.json",
                        ["travelRoutes"] = "travel-routes.json",
                        ["mapGraveyards"] = "map-graveyards.json",
                        ["storageUpgrades"] = "storage-upgrades.json",
                        ["staticObjectTypes"] = "static-object-types.json",
                        ["items"] = "items.json",
                    }),
                Path.Combine(dir, DataManifest.FileName));
            Write(dir, "maps.json", new[] { new MapDefinition(7, "starter") });
            Write(dir, "spells.json", Array.Empty<object>());
            Write(dir, "creatures.json", new[] { new CreatureDefinition(12, "wolf", 3, 80) });
            Write(dir, "npcs.json", npcs ?? [new NpcDefinition(12, NpcFlags.Teleporter)]);
            Write(dir, "quests.json", quests ?? [new QuestDefinition(5001, 12, 1, 2, 1)]);
            Write(dir, "quest-rewards.json", rewards ??
            [
                new QuestRewardDefinition(5001, 250, [new QuestRewardItem(101, 1, 5101, 1)], []),
            ]);
            Write(dir, "teleport-stops.json", stops ??
            [
                new TeleportStopDefinition(1, 7, 1, 2, 3),
                new TeleportStopDefinition(2, 7, 9, 2, 3),
            ]);
            Write(dir, "travel-routes.json", routes ?? [new TravelRouteDefinition(1, 1, 2, 70)]);
            Write(dir, "map-graveyards.json", graveyards ??
                [new MapGraveyardDefinition(1, 7, 1, 2, 3, 0, ZoneId: 0, Faction: 0)]);
            Write(dir, "storage-upgrades.json", storageUpgrades ??
                [new StorageUpgradeDefinition(1, "bag", 18, 12, PriceCurrency: 0, PriceAmount: 100)]);
            Write(dir, "static-object-types.json", staticObjectTypes ??
                [new StaticObjectTypeDefinition(145, StaticObjectTypes.StorageBox)]);
            Write(dir, "items.json", items ?? []);

            assert(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Write<T>(string directory, string file, IReadOnlyList<T> values) =>
        File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(values));
}
