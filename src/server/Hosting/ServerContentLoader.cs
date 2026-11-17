using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.World;

namespace Ubiquitous.Server;

public sealed class ServerContentLoader(
    DataCatalog? catalog, ServerOptions options, ILogger logger, string contentDirectory)
{
    public ServerContent Load()
    {
        var staticObjectTypes = LoadOptional("static object types", cat => cat.LoadStaticObjectTypes());
        var creatureSpawns = LoadCreatureSpawns();
        var (staticObjectSpawns, hiddenStaticObjects) = LoadStaticObjectSpawns(staticObjectTypes);
        var patrols = LoadPatrolRoutes();
        var disabledRaces = options.DisabledRacesFor(catalog?.Manifest.GameVersion);
        var game = LoadGameContent(staticObjectTypes) with
        {
            HiddenStaticObjects = hiddenStaticObjects,
            PlayableRaces = PlayerRaceIds.All.Where(race => !disabledRaces.Contains(race)).ToHashSet(),
            WaypointRoutes = patrols.All,
        };
        game = WithRates(game, options.ResolvedRates);

        return new ServerContent(game, creatureSpawns, staticObjectSpawns, LoadWalkableGround()) { Patrols = patrols };
    }

    // Creature health is scaled once here, so combat, the spawn message and
    // every other reader of a creature's health agree on it.
    public static GameContent WithRates(GameContent game, GameRates rates) => game with
    {
        Rates = rates,
        Creatures = rates.CreatureHealth == 1.0
            ? game.Creatures
            : game.Creatures.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { MaxHealth = rates.ScaleCreatureHealth(pair.Value.MaxHealth) }),
    };

    public static IReadOnlyList<CreatureSpawnDefinition> FilterFestiveCreatures(
        IReadOnlyList<CreatureSpawnDefinition> spawns, bool enabled, IReadOnlyList<uint>? festive)
    {
        if (enabled || festive is not { Count: > 0 }) return spawns;
        return [.. spawns.Where(spawn => !festive.Contains(spawn.CreatureEntryId))];
    }

    public static IReadOnlyList<StaticObjectSpawnDefinition> FilterFestiveSpawns(
        IReadOnlyList<StaticObjectSpawnDefinition> spawns, bool enabled, IReadOnlyList<uint>? festive)
    {
        if (enabled || festive is not { Count: > 0 }) return spawns;
        return [.. spawns.Where(spawn => !festive.Contains(spawn.EntryId))];
    }

    private IReadOnlyList<CreatureSpawnDefinition> LoadCreatureSpawns()
    {
        var loaded = LoadOptional("creature spawns", cat => cat.LoadCreatureSpawns())
            ?? DevelopmentFixtures.CreatureSpawns;
        var seasonal = LoadOptional("creatures", cat => cat.LoadCreatures())?
            .Where(creature => FestivalIds.IsSeasonal(creature.FestivalId))
            .Select(creature => creature.EntryId)
            .ToArray();
        var kept = FilterFestiveCreatures(
            loaded,
            options.FestivalContentEnabled,
            options.FestivalCreatureEntries ?? NonEmpty(seasonal) ?? FestivalContent.CreatureEntries);
        if (kept.Count != loaded.Count)
            logger.LogInformation("Festive creatures off: {Count} spawn(s) hidden", loaded.Count - kept.Count);
        return kept;
    }

    private (IReadOnlyList<StaticObjectSpawnDefinition> Shown, IReadOnlyList<StaticObjectSpawnDefinition> Hidden)
        LoadStaticObjectSpawns(IReadOnlyList<StaticObjectTypeDefinition>? types)
    {
        var spawns = LoadOptional("static object spawns", cat => cat.LoadStaticObjectSpawns())
            ?? DevelopmentFixtures.StaticObjectSpawns;
        var seasonal = types?
            .Where(type => FestivalIds.IsSeasonal(type.Festival))
            .Select(type => type.Entry)
            .ToArray();
        var shown = FilterFestiveSpawns(
            spawns,
            options.FestivalContentEnabled,
            options.FestivalStaticEntries ?? NonEmpty(seasonal) ?? FestivalContent.StaticObjectEntries);
        if (shown.Count == spawns.Count) return (shown, []);

        var shownIds = shown.Select(spawn => spawn.Id).ToHashSet();
        var hidden = spawns.Where(spawn => !shownIds.Contains(spawn.Id)).ToArray();
        logger.LogInformation("Festive scenery off: {Count} spawn(s) hidden", hidden.Length);
        return (shown, hidden);
    }

    private GameContent LoadGameContent(IReadOnlyList<StaticObjectTypeDefinition>? staticObjectTypes)
    {
        var fallback = GameContent.Development;
        if (catalog is null) return fallback;

        var maps = LoadOptional("maps", cat => cat.LoadMaps().ToDictionary(map => map.Id));
        var creatures = LoadOptional("creatures", cat => cat.LoadCreatures().ToDictionary(creature => creature.EntryId));
        var npcs = LoadOptional("npcs", cat => cat.LoadNpcs().ToDictionary(npc => npc.EntryId));
        var quests = LoadOptional("quests", cat => cat.LoadQuests());
        var rewards = LoadOptional("quest rewards", cat => cat.LoadQuestRewards().ToDictionary(reward => reward.QuestId));
        var stops = LoadOptional("teleport stops", cat => cat.LoadTeleportStops().ToDictionary(stop => stop.Id));
        var routes = LoadOptional("travel routes", cat => cat.LoadTravelRoutes());
        var graveyards = LoadOptional("map graveyards", cat => cat.LoadMapGraveyards());
        var gossipMenus = LoadOptional("gossip menus", cat => cat.LoadGossipMenus().ToDictionary(menu => menu.Id));
        var spells = LoadOptional("spells", cat => cat.LoadSpells().ToDictionary(spell => spell.Id));
        var items = LoadOptional("items", cat => cat.LoadItems().ToDictionary(item => item.Id));
        var vendorTabs = LoadOptional("vendor stock", cat => cat.LoadVendorStock()
            .GroupBy(stock => stock.SellerEntryId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<VendorTab>)[.. group.Select(stock => stock.Tab)]));
        var storageUpgrades = LoadOptional("storage upgrades", cat => cat.LoadStorageUpgrades());
        var talentTrees = LoadOptional("talent trees", cat => cat.LoadTalentTrees());
        var playerLooks = LoadOptional("player looks", cat => cat.LoadPlayerLooks());
        var questStartersByStaticObject = LoadOptional("static object quest starters",
            cat => cat.LoadStaticObjectQuestStarters());
        var questFinishersByStaticObject = LoadOptional("static object quest finishers",
            cat => cat.LoadStaticObjectQuestFinishers());
        var questItemBindingsByStaticObject = LoadOptional("static object quest item bindings",
            cat => cat.LoadStaticObjectQuestItemBindings());
        var questFinishersByCreature = LoadOptional("creature quest finishers",
            cat => cat.LoadCreatureQuestFinishers());
        var lootTables = catalog.Manifest.Datasets.ContainsKey("creatureLootTables")
            || DataManifest.PlaysAsSeen(catalog.Manifest.GameVersion)
            ? LoadOptional("creature loot tables", cat => cat.LoadCreatureLootTables().ToDictionary(table => table.CreatureEntryId))
            : null;

        var seenBuild = DataManifest.PlaysAsSeen(catalog.Manifest.GameVersion);
        if (catalog.Manifest.GameVersion == "unknown")
        {
            logger.LogWarning(
                "The data package's tables match no build this server knows; it plays as 1.1.4 does. " +
                "Fingerprint the build to have its own spells, cap and classes read.");
        }
        StartingSpellbook? book = null;
        if (spells is not null && seenBuild) StartingLoadouts.ValidateAgainst(spells);
        else if (spells is not null)
        {
            book = StartingSpellbook.Read(
                spells,
                items ?? throw new InvalidOperationException("A build other than 1.1.4 needs its items to start a character."),
                staticObjectTypes ?? throw new InvalidOperationException(
                    "A build other than 1.1.4 needs its static object types to start a character."),
                StartingLevelsByClass(talentTrees ?? fallback.TalentTrees));
            logger.LogInformation("Starting spells read from the {GameVersion} tables: classes {Classes}",
                catalog.Manifest.GameVersion, string.Join(", ", book.Classes.Keys.Order()));
        }

        var travelLoaded = stops is not null && routes is not null;

        // A later build ships no loot tables; its loot is made from the rest of its data unless the host says no.
        var generated = GeneratedLoot.None;
        if (lootTables is null && !seenBuild && options.GeneratedLootEnabled && creatures is not null && items is not null)
        {
            generated = LootGenerator.Build(creatures, items, spells ?? fallback.Spells,
                npcs ?? new Dictionary<uint, NpcDefinition>(), rewards ?? new Dictionary<uint, QuestRewardDefinition>(),
                vendorTabs ?? new Dictionary<uint, IReadOnlyList<VendorTab>>(),
                LoadOptional("quest item sources", cat => cat.LoadQuestItemSources()) ?? [],
                options.ResolvedLootRules);
            logger.LogInformation("Loot made for {Creatures} creature(s), {QuestItems} of their quest items named by quests",
                generated.Tables.Count, generated.QuestItems.Count);
        }

        return fallback with
        {
            StartingCurrency = 0,
            Creatures = creatures ?? fallback.Creatures,
            Npcs = npcs ?? fallback.Npcs,
            Quests = quests ?? fallback.Quests,
            QuestRewards = rewards ?? fallback.QuestRewards,
            Stops = travelLoaded ? stops! : fallback.Stops,
            Routes = travelLoaded ? routes! : fallback.Routes,
            VendorTabs = vendorTabs ?? fallback.VendorTabs,
            Spells = WithOperatorSpells(spells ?? fallback.Spells, fallback.Spells),
            // The development spellbook names 1.1.4 ids; a later build starts with its own and the GM spells.
            StartingSpells = book is not null ? [.. OperatorSpells.All] : fallback.StartingSpells,
            Items = items ?? fallback.Items,
            StorageUpgrades = storageUpgrades ?? fallback.StorageUpgrades,
            BankBoxEntries = EntriesOfType(staticObjectTypes, StaticObjectTypes.StorageBox) ?? fallback.BankBoxEntries,
            MailBoxEntries = EntriesOfType(staticObjectTypes, StaticObjectTypes.MailBox) ?? fallback.MailBoxEntries,
            SummonStoneEntries = EntriesOfType(staticObjectTypes, StaticObjectTypes.SummonStone) ?? fallback.SummonStoneEntries,
            StaticObjectTypesByEntry = staticObjectTypes?.ToDictionary(type => type.Entry, type => type.Type)
                ?? fallback.StaticObjectTypesByEntry,
            QuestStartersByStaticObject = questStartersByStaticObject ?? fallback.QuestStartersByStaticObject,
            QuestFinishersByStaticObject = questFinishersByStaticObject ?? fallback.QuestFinishersByStaticObject,
            QuestItemBindingsByStaticObject = questItemBindingsByStaticObject ?? fallback.QuestItemBindingsByStaticObject,
            QuestFinishersByCreature = questFinishersByCreature ?? fallback.QuestFinishersByCreature,
            Maps = maps ?? fallback.Maps,
            LootTables = lootTables ?? (seenBuild ? fallback.LootTables : generated.Tables),
            GeneratedQuestItems = generated.QuestItems,
            Graveyards = graveyards ?? fallback.Graveyards,
            GossipMenus = gossipMenus ?? fallback.GossipMenus,
            TalentTrees = talentTrees ?? fallback.TalentTrees,
            PlayerLooks = playerLooks ?? fallback.PlayerLooks,
            Terrain = LoadTerrain(),
            Loadouts = book is not null ? StartingLoadouts.FromSpellbook(book, items!) : fallback.Loadouts,
            Spellbook = book,
            MiningGatherSpellId = book?.MiningGather ?? fallback.MiningGatherSpellId,
            LevelCap = LevelingService.CapOf(catalog.Manifest),
        };
    }

    // The classes a build plays are those its talent trees belong to.
    public static IReadOnlyDictionary<int, int> StartingLevelsByClass(IReadOnlyList<TalentTreeDefinition> trees) =>
        trees.Select(tree => tree.ClassId).Distinct().Order().ToDictionary(
            playerClass => playerClass,
            playerClass => playerClass == PlayerClassIds.FlameKnight
                ? StartingLoadouts.FlameKnightStartingLevel
                : CharacterCreation.StartingLevel);

    private PatrolRouteLibrary LoadPatrolRoutes()
    {
        var directory = Path.Combine(contentDirectory, PatrolRouteFiles.DirectoryName);
        var routes = PatrolRouteLibrary.Load(directory);
        logger.LogInformation("Patrol routes: {Count} placed route(s) from {Directory}", routes.All.Count, directory);
        return routes;
    }

    private ITerrainHeightSource? LoadTerrain()
    {
        var directory = catalog?.TerrainDirectory;
        if (directory is null || !Directory.Exists(directory)) return null;

        try
        {
            var terrain = TerrainHeightSource.LoadFromDirectory(directory);
            if (terrain.MapCount == 0) return null;
            logger.LogInformation("Terrain: {Count} map(s)", terrain.MapCount);
            return terrain;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning("Terrain failed to load ({Reason}); creatures keep their spawn height", ex.Message);
            return null;
        }
    }

    private WalkableGroundSource? LoadWalkableGround()
    {
        if (catalog?.Directory is null) return null;
        var directory = Path.Combine(catalog.Directory, NavigationPackage.DirectoryName);
        if (!Directory.Exists(directory)) return null;

        try
        {
            var ground = WalkableGroundSource.LoadFromDirectory(directory);
            if (ground.MapCount == 0) return null;
            logger.LogInformation("Walkable ground: {Count} map(s)", ground.MapCount);
            return ground;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Walkable ground failed to load ({Reason}); creatures walk straight lines", ex.Message);
            return null;
        }
    }

    private T? LoadOptional<T>(string label, Func<DataCatalog, T> load)
        where T : class
    {
        if (catalog is null) return null;
        try
        {
            return load(catalog);
        }
        catch (DatasetNotListedException ex)
        {
            logger.LogWarning("{Dataset} unavailable ({Reason}); using development fallback", label, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or JsonException)
        {
            throw new InvalidDataException($"The listed dataset '{label}' could not be loaded: {ex.Message}", ex);
        }
    }

    private static HashSet<uint>? EntriesOfType(IReadOnlyList<StaticObjectTypeDefinition>? types, uint type) =>
        types?.Where(definition => definition.Type == type).Select(definition => definition.Entry).ToHashSet();

    private static IReadOnlyList<uint>? NonEmpty(IReadOnlyList<uint>? entries) =>
        entries is { Count: > 0 } ? entries : null;

    private static IReadOnlyDictionary<uint, SpellDefinition> WithOperatorSpells(
        IReadOnlyDictionary<uint, SpellDefinition> loaded,
        IReadOnlyDictionary<uint, SpellDefinition> authored)
    {
        var merged = new Dictionary<uint, SpellDefinition>(loaded);
        var changed = false;
        foreach (var spellId in OperatorSpells.All)
        {
            if (!authored.TryGetValue(spellId, out var spell)) continue;
            if (merged.TryGetValue(spellId, out var present) && present == spell) continue;
            merged[spellId] = spell;
            changed = true;
        }

        return changed ? merged : loaded;
    }
}
