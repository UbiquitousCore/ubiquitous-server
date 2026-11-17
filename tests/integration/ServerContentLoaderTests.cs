using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ServerContentLoaderTests
{
    [Fact]
    public void WithNoDataPackage_ServesDevelopmentSpawns()
    {
        var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

        Assert.Equal(DevelopmentFixtures.CreatureSpawns, content.CreatureSpawns);
        Assert.Equal(DevelopmentFixtures.StaticObjectSpawns, content.StaticObjectSpawns);
    }

    [Fact]
    public void TheCreatureHealthRate_ScalesEveryCreatureOnceAtLoad()
    {
        var rates = new GameRates(Xp: 1.5, CreatureHealth: 2.0);
        var options = ServerOptions.Defaults with { Rates = rates };

        var normal = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();
        var scaled = new ServerContentLoader(null, options, NullLogger.Instance, ContentDirectory.Find()).Load();

        Assert.Equal(rates, scaled.Game.Rates);
        Assert.NotEmpty(normal.Game.Creatures);
        foreach (var (entry, creature) in normal.Game.Creatures)
            Assert.Equal(creature.MaxHealth * 2, scaled.Game.Creatures[entry].MaxHealth);
    }

    [Fact]
    public void APlacedPatrolRouteFile_IsServed()
    {
        var root = Directory.CreateTempSubdirectory("ubiquitous-content-");
        try
        {
            var routes = Directory.CreateDirectory(Path.Combine(root.FullName, "patrol-routes"));
            File.WriteAllText(Path.Combine(routes.FullName, "test.json"), """
                {"mapId": 13, "routes": [{"id": 4541, "stops": [
                  {"x": 0, "y": 0, "z": 0, "pauseSeconds": 6},
                  {"x": 10, "y": 0, "z": 0, "pauseSeconds": 0}]}]}
                """);
            var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, root.FullName).Load();

            var route = Assert.Single(content.Game.WaypointRoutes).Value;
            Assert.Equal(4541u, route.Id);
            Assert.Equal(6000, route.Stops[0].PauseMilliseconds);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlacedPatrolRoutes_ComeFromTheContentDirectoryTheLoaderIsGiven()
    {
        var empty = Directory.CreateTempSubdirectory("ubiquitous-content-");
        try
        {
            var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, empty.FullName).Load();

            Assert.Empty(content.Game.WaypointRoutes);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADatasetTheManifestListsButCannotBeRead_StopsTheServerLoadingContent()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            File.WriteAllText(Path.Combine(package.FullName, "maps.json"), "{ not json");
            var catalog = new DataCatalog(Manifest(new() { ["maps"] = "maps.json" }), package.FullName);

            var loader = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find());

            var error = Assert.Throws<InvalidDataException>(loader.Load);
            Assert.Contains("maps", error.Message);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADatasetTheManifestDoesNotList_StillFallsBackToDevelopmentContent()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var catalog = new DataCatalog(Manifest(new()), package.FullName);

            var content = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

            Assert.Equal(DevelopmentFixtures.CreatureSpawns, content.CreatureSpawns);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARealPackage_StartsNewCharactersWithNoMoney()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var catalog = new DataCatalog(Manifest(new()), package.FullName);

            var content = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load();

            Assert.Equal(0, content.Game.StartingCurrency);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    private static DataManifest Manifest(Dictionary<string, string> datasets) =>
        new("test", "1.1.4", "hash", DateTimeOffset.UnixEpoch, datasets);

    private static void Write<T>(DirectoryInfo package, Dictionary<string, string> datasets, string dataset, T value)
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        File.WriteAllText(Path.Combine(package.FullName, dataset + ".json"), System.Text.Json.JsonSerializer.Serialize(value, options));
        datasets[dataset] = dataset + ".json";
    }

    // A later build in miniature: one mage chain, the general spells, an ore node and one tree.
    private static DataCatalog LaterBuild(DirectoryInfo package)
    {
        var datasets = new Dictionary<string, string>();
        Write(package, datasets, "spells", new SpellDefinition[]
        {
            new(1, "Gathering", 2000, 1500, 3.6f),
            new(2, "Gathering", 3500, 1500, 5f),
            new(4, "Switch Target", 0, 0, 60f),
            new(5, "Auto Attack", 0, 3000, 0f),
            new(7, "Auto Shoot", 0, 3000, 25f, WornSlotNeeded: EquipmentSlots.Ranged, RequiredItemClass: 2,
                ItemSubclassesAllowed: 1u << 20),
            new(10000, "Fireball", 2000, 0, 25f, Rank: 1),
            new(10012, "Fireball", 2000, 0, 25f, Rank: 2, ChainFirstSpellId: 10000),
            new(10020, "Learn Fireball II", 0, 0, 0f, EffectLearnSpellId: 10012),
        });
        Write(package, datasets, "items", new ItemDefinition[]
        {
            new(9000, "lesson", 9, 0, 0, OnUseSpellId: 10020, AllowableClassMask: ItemClassMask.BitFor(PlayerClassIds.Mage),
                RequiredLevel: 2),
        });
        Write(package, datasets, "staticObjectTypes", new StaticObjectTypeDefinition[]
        {
            new(MiningOres.Bronze.NodeEntry, 1, GatherSpellId: 2),
            new(3, 6, GatherSpellId: 1),
        });
        Write(package, datasets, "talentTrees", new TalentTreeDefinition[] { new(137, PlayerClassIds.Mage, [new(1, 1, 1, 0, [], [10000])]) });
        return new DataCatalog(
            new DataManifest("test", "4.2.21", "hash", DateTimeOffset.UnixEpoch, datasets, LevelCap: 75), package.FullName);
    }

    [Fact]
    public void ALaterBuild_StartsItsCharactersWithItsOwnSpells_AndOpensMendel()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var content = new ServerContentLoader(
                LaterBuild(package), ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load().Game;

            Assert.Contains(PlayerRaceIds.Mendel, content.PlayableRaces);
            var mage = content.LoadoutFor(PlayerRaceIds.Mendel, PlayerClassIds.Mage)!;
            Assert.Equal([10000u, 5u, 7u, 4u, 1u, 2u], mage.Spells);
            Assert.Equal([new StartingActionButton(0, 5), new StartingActionButton(1, 10000)], mage.ActionBar);
            Assert.Null(content.LoadoutFor(PlayerRaceIds.Human, PlayerClassIds.Warrior));
            Assert.Equal(2u, content.MiningGatherSpellId);
            Assert.Equal(75, content.LevelCap);
            Assert.False(content.PlaysAsSeen);
            Assert.Equal(OperatorSpells.All, content.StartingSpells);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ALaterBuildWithoutLootTables_GetsLootMadeFromItsData_OnlyWhenTheHostAsks()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var catalog = LaterBuild(package);
            var datasets = new Dictionary<string, string>(catalog.Manifest.Datasets);
            Write(package, datasets, "creatures", new CreatureDefinition[]
            {
                new(20, "Gray Wolf", 10, 100, FactionType: UnitFactionTypes.Hostile),
                new(21, "Sheriff", 10, 100, FactionType: UnitFactionTypes.Townsfolk),
            });
            Write(package, datasets, "items", new ItemDefinition[]
            {
                new(9000, "lesson", 9, 0, 0, OnUseSpellId: 10020, AllowableClassMask: ItemClassMask.BitFor(PlayerClassIds.Mage),
                    RequiredLevel: 2),
                new(9100, "Wolf Pelt", 10, 0, 0),
                new(9200, "Hide Vest", ItemClasses.Armour, 1, 0, SellPrice: 5, InventoryType: InventoryTypes.Chest,
                    RequiredLevel: 9, Quality: 2),
            });
            Write(package, datasets, QuestItemSourceDefinition.Dataset, new QuestItemSourceDefinition[] { new(7, 9100, [20]) });
            catalog = new DataCatalog(catalog.Manifest with { Datasets = datasets }, package.FullName);

            var made = new ServerContentLoader(catalog, ServerOptions.Defaults with { GeneratedLootEnabled = true },
                NullLogger.Instance, ContentDirectory.Find()).Load().Game;
            var none = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find())
                .Load().Game;

            Assert.Equal([20u], made.LootTables.Keys);
            Assert.Equal([9100u, 9200u], made.LootTables[20].Groups.SelectMany(group => group.Items).Select(item => item.ItemId));
            Assert.Equal([7u], made.GeneratedQuestItems[9100]);
            Assert.Empty(none.LootTables);
            Assert.Empty(none.GeneratedQuestItems);

            var hosted = ServerOptions.Load([], new Dictionary<string, string?>
            {
                ["UBIQUITOUS_GENERATED_LOOT"] = "true",
                ["UBIQUITOUS_LOOT_QUEST_ITEM_PERCENT"] = "90",
                ["UBIQUITOUS_LOOT_DROP_RATE"] = "2",
            });
            var richer = new ServerContentLoader(catalog, hosted, NullLogger.Instance, ContentDirectory.Find()).Load().Game;
            var items = richer.LootTables[20].Groups.SelectMany(group => group.Items).ToDictionary(item => item.ItemId);
            Assert.Equal(90, items[9100].DropChancePercent);
            Assert.Equal(2 * LootGeneratorRules.Default.Gear[CreatureRank.Ordinary].ChancePercent, items[9200].DropChancePercent);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void ALaterBuild_StillClosesARaceTheHostClosed()
    {
        var package = Directory.CreateTempSubdirectory("ubiquitous-package-");
        try
        {
            var options = ServerOptions.Defaults with { DisabledRaces = [PlayerRaceIds.Mendel] };
            var content = new ServerContentLoader(
                LaterBuild(package), options, NullLogger.Instance, ContentDirectory.Find()).Load().Game;

            Assert.DoesNotContain(PlayerRaceIds.Mendel, content.PlayableRaces);
        }
        finally
        {
            package.Delete(recursive: true);
        }
    }

    [Fact]
    public void WithNoPackageOrA114One_MendelStaysClosed_AndTheSeenLoadoutsServe()
    {
        var content = new ServerContentLoader(null, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find()).Load().Game;

        Assert.DoesNotContain(PlayerRaceIds.Mendel, content.PlayableRaces);
        Assert.Same(StartingLoadouts.All, content.Loadouts);
        Assert.Equal(LevelingService.SeenMaxLevel, content.LevelCap);
        Assert.Equal([PlayerRaceIds.Mendel], ServerOptions.Defaults.DisabledRacesFor(DataManifest.TargetGameVersion));
        Assert.Empty(ServerOptions.Defaults.DisabledRacesFor("4.2.21"));
    }

    [Fact]
    public void TheContentDirectory_IsTheRepositorysOwnWhenRunFromIt()
    {
        var found = ContentDirectory.Find();

        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(found)!, "ubiquitous-server.sln")));
        Assert.Equal("content", Path.GetFileName(found));
    }

    [Fact]
    public void TheContentDirectory_IsTheOneBesideTheServerOutsideARepository()
    {
        var outside = Directory.CreateTempSubdirectory("ubiquitous-published-");
        try
        {
            Assert.Equal(Path.Combine(outside.FullName, "content"), ContentDirectory.Find(outside.FullName));
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }
}
