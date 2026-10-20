using System.Collections.Concurrent;
using System.Text.Json;

namespace Ubiquitous.Data;

public sealed class DataValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
}

public sealed class DataCatalog
{
    private readonly ConcurrentDictionary<(string Dataset, Type Type), object> _parsed = new();

    public DataManifest Manifest { get; }
    public string? Directory { get; }

    public DataCatalog(DataManifest manifest, string? directory = null)
    {
        Manifest = manifest;
        Directory = directory;
    }

    public string? TerrainDirectory =>
        Directory is null ? null : Path.Combine(Directory, TerrainPackage.DirectoryName);

    public static DataCatalog LoadFromDirectory(string directory)
    {
        string manifestPath = Path.Combine(directory, DataManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"No {DataManifest.FileName} found under '{directory}'. Run the data pipeline first (docs/development/setup.md).");
        }

        return new DataCatalog(DataManifestJson.Load(manifestPath), Path.GetFullPath(directory));
    }

    public DataValidationResult Validate()
    {
        var result = new DataValidationResult();

        if (string.IsNullOrWhiteSpace(Manifest.ExtractorVersion))
        {
            result.Errors.Add("manifest.extractorVersion is missing.");
        }
        if (string.IsNullOrWhiteSpace(Manifest.GameVersion))
        {
            result.Errors.Add("manifest.gameVersion is missing.");
        }
        if (string.IsNullOrWhiteSpace(Manifest.SourceHash))
        {
            result.Errors.Add("manifest.sourceHash is missing.");
        }

        if (Manifest.Datasets is null)
        {
            result.Errors.Add("manifest.datasets is missing.");
            return result;
        }

        foreach (string required in DataManifest.RequiredDatasets)
        {
            if (!Manifest.Datasets.TryGetValue(required, out var file) || string.IsNullOrWhiteSpace(file))
            {
                result.Errors.Add($"Required dataset '{required}' is not listed in the manifest.");
                continue;
            }

            if (Directory is not null)
            {
                if (!TryResolveDatasetPath(file, out var path, out var error))
                {
                    result.Errors.Add($"Dataset '{required}' {error}");
                }
                else if (!File.Exists(path))
                {
                    result.Errors.Add($"Dataset '{required}' file '{file}' does not exist.");
                }
            }
        }

        foreach (string optional in OptionalDatasets)
        {
            if (Manifest.Datasets.TryGetValue(optional, out var file) && !string.IsNullOrWhiteSpace(file) && Directory is not null)
            {
                if (!TryResolveDatasetPath(file, out var path, out var error))
                {
                    result.Errors.Add($"Optional dataset '{optional}' {error}");
                }
                else if (!File.Exists(path))
                {
                    result.Errors.Add($"Optional dataset '{optional}' file '{file}' does not exist.");
                }
            }
        }

        return result;
    }

    public static readonly IReadOnlyList<string> OptionalDatasets = new[]
    {
        "creatureSpawns",
        "staticObjectSpawns",
        "npcs",
        "vendorStock",
        "staticObjectQuestStarters",
        "staticObjectQuestFinishers",
        "staticObjectQuestItemBindings",
        "creatureQuestFinishers",
        "creatureLootTables",
        "quests",
        "questRewards",
        "teleportStops",
        "gossipMenus",
        "travelRoutes",
        "mapGraveyards",
        "items",
        "storageUpgrades",
        "staticObjectTypes",
        "talentTrees",
        PlayerLookDefinition.Dataset,
    };

    public string DatasetPath(string name)
    {
        if (Directory is null)
        {
            throw new InvalidOperationException("Dataset paths are available only for a directory-backed catalog.");
        }

        if (!Manifest.Datasets.TryGetValue(name, out var file))
            throw new DatasetNotListedException(name);

        if (!TryResolveDatasetPath(file, out var path, out var error))
            throw new InvalidDataException($"Dataset '{name}' {error}");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Dataset '{name}' file '{file}' does not exist.", path);
        }

        return path;
    }

    public IReadOnlyList<MapDefinition> LoadMaps() =>
        LoadDefinitions<MapDefinition>("maps", "maps");

    public IReadOnlyList<CreatureDefinition> LoadCreatures() =>
        LoadDefinitions<CreatureDefinition>("creatures", "creatures");

    public IReadOnlyList<SpellDefinition> LoadSpells() =>
        LoadDefinitions<SpellDefinition>("spells", "spells");

    public IReadOnlyList<ItemDefinition> LoadItems() =>
        LoadDefinitions<ItemDefinition>("items", "items");

    public IReadOnlyList<CreatureSpawnDefinition> LoadCreatureSpawns()
    {
        var mapIds = LoadMaps().Select(map => map.Id).ToHashSet();
        var creatureIds = LoadCreatures().Select(creature => creature.EntryId).ToHashSet();
        var spawns = LoadDefinitions<CreatureSpawnDefinition>("creatureSpawns", "creature spawns");

        foreach (var spawn in spawns)
        {
            ValidateMapReference("creatureSpawns", spawn.Id, spawn.MapId, mapIds);
            if (!creatureIds.Contains(spawn.CreatureEntryId))
            {
                throw new InvalidDataException(
                    $"Dataset 'creatureSpawns' references missing creature {spawn.CreatureEntryId} from spawn {spawn.Id}.");
            }

            ValidatePosition("creatureSpawns", spawn.Id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        }

        return spawns;
    }

    public IReadOnlyList<StaticObjectSpawnDefinition> LoadStaticObjectSpawns()
    {
        var mapIds = LoadMaps().Select(map => map.Id).ToHashSet();
        var spawns = LoadDefinitions<StaticObjectSpawnDefinition>("staticObjectSpawns", "static object spawns");

        foreach (var spawn in spawns)
        {
            ValidateMapReference("staticObjectSpawns", spawn.Id, spawn.MapId, mapIds);
            ValidatePosition("staticObjectSpawns", spawn.Id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        }

        return spawns;
    }

    public IReadOnlyList<NpcDefinition> LoadNpcs()
    {
        var creatureIds = LoadCreatures().Select(creature => creature.EntryId).ToHashSet();
        var npcs = LoadDefinitions<NpcDefinition>("npcs", "npcs");

        foreach (var npc in npcs)
        {
            if (!creatureIds.Contains(npc.EntryId))
            {
                throw new InvalidDataException(
                    $"Dataset 'npcs' references missing creature {npc.EntryId}.");
            }
        }

        return npcs;
    }

    public IReadOnlyList<VendorStockDefinition> LoadVendorStock()
    {
        var npcs = LoadNpcs().ToDictionary(npc => npc.EntryId);
        var stock = LoadDefinitions<VendorStockDefinition>("vendorStock", "vendor stock");

        foreach (var entry in stock)
        {
            if (!npcs.TryGetValue(entry.SellerEntryId, out var npc) || !npc.Flags.HasFlag(NpcFlags.Vendor))
            {
                throw new InvalidDataException(
                    $"Dataset 'vendorStock' references seller {entry.SellerEntryId} that is not a known vendor NPC.");
            }
        }

        return stock;
    }

    public IReadOnlyList<StaticObjectQuestBindingDefinition> LoadStaticObjectQuestStarters() =>
        LoadStaticObjectQuestBindings("staticObjectQuestStarters");

    public IReadOnlyList<StaticObjectQuestBindingDefinition> LoadStaticObjectQuestFinishers() =>
        LoadStaticObjectQuestBindings("staticObjectQuestFinishers");

    public IReadOnlyList<StaticObjectItemBindingDefinition> LoadStaticObjectQuestItemBindings()
    {
        var path = DatasetPath("staticObjectQuestItemBindings");
        List<StaticObjectItemBindingDefinition>? bindings;
        try
        {
            using var stream = File.OpenRead(path);
            bindings = JsonSerializer.Deserialize<List<StaticObjectItemBindingDefinition>>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Dataset 'staticObjectQuestItemBindings' is not valid JSON: {ex.Message}", ex);
        }

        if (bindings is null)
        {
            throw new InvalidDataException("Dataset 'staticObjectQuestItemBindings' must contain a JSON array.");
        }

        var staticObjectEntries = LoadStaticObjectTypes().Select(t => t.Entry).ToHashSet();
        foreach (var binding in bindings)
        {
            if (!staticObjectEntries.Contains(binding.StaticObjectEntry))
            {
                throw new InvalidDataException(
                    $"Dataset 'staticObjectQuestItemBindings' references missing static object {binding.StaticObjectEntry}.");
            }
        }

        return bindings;
    }

    private IReadOnlyList<StaticObjectQuestBindingDefinition> LoadStaticObjectQuestBindings(string dataset)
    {
        var path = DatasetPath(dataset);
        List<StaticObjectQuestBindingDefinition>? bindings;
        try
        {
            using var stream = File.OpenRead(path);
            bindings = JsonSerializer.Deserialize<List<StaticObjectQuestBindingDefinition>>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Dataset '{dataset}' is not valid JSON: {ex.Message}", ex);
        }

        if (bindings is null)
        {
            throw new InvalidDataException($"Dataset '{dataset}' must contain a JSON array.");
        }

        var staticObjectEntries = LoadStaticObjectTypes().Select(t => t.Entry).ToHashSet();
        foreach (var binding in bindings)
        {
            if (!staticObjectEntries.Contains(binding.StaticObjectEntry))
            {
                throw new InvalidDataException(
                    $"Dataset '{dataset}' references missing static object {binding.StaticObjectEntry}.");
            }
        }

        return bindings;
    }

    public IReadOnlyList<CreatureQuestBindingDefinition> LoadCreatureQuestFinishers()
    {
        var path = DatasetPath("creatureQuestFinishers");
        List<CreatureQuestBindingDefinition>? bindings;
        try
        {
            using var stream = File.OpenRead(path);
            bindings = JsonSerializer.Deserialize<List<CreatureQuestBindingDefinition>>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Dataset 'creatureQuestFinishers' is not valid JSON: {ex.Message}", ex);
        }

        if (bindings is null)
        {
            throw new InvalidDataException("Dataset 'creatureQuestFinishers' must contain a JSON array.");
        }

        var creatureEntries = LoadCreatures().Select(c => c.EntryId).ToHashSet();
        foreach (var binding in bindings)
        {
            if (!creatureEntries.Contains(binding.CreatureEntry))
            {
                throw new InvalidDataException(
                    $"Dataset 'creatureQuestFinishers' references missing creature {binding.CreatureEntry}.");
            }
        }

        return bindings;
    }

    public IReadOnlyList<CreatureLootTableDefinition> LoadCreatureLootTables()
    {
        var creatureEntries = LoadCreatures().Select(c => c.EntryId).ToHashSet();
        var itemIds = LoadItems().Select(i => i.Id).ToHashSet();
        var tables = LoadDefinitions<CreatureLootTableDefinition>("creatureLootTables", "creature loot tables");

        foreach (var table in tables)
        {
            if (!creatureEntries.Contains(table.CreatureEntryId))
            {
                throw new InvalidDataException(
                    $"Dataset 'creatureLootTables' references missing creature {table.CreatureEntryId}.");
            }

            foreach (var group in table.Groups)
            {
                if (group.GuaranteedDrops < 0 || group.MaxDrops < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset 'creatureLootTables' contains creature {table.CreatureEntryId} with a negative group count.");
                }

                foreach (var item in group.Items)
                {
                    if (!itemIds.Contains(item.ItemId))
                    {
                        throw new InvalidDataException(
                            $"Dataset 'creatureLootTables' contains creature {table.CreatureEntryId} with missing item {item.ItemId}.");
                    }

                    if (item.MinDropCount > item.MaxDropCount || item.MinDropCount <= 0)
                    {
                        throw new InvalidDataException(
                            $"Dataset 'creatureLootTables' contains creature {table.CreatureEntryId} with an invalid count range for item {item.ItemId}.");
                    }

                    if (item.DropChancePercent <= 0 || item.DropChancePercent > 100)
                    {
                        throw new InvalidDataException(
                            $"Dataset 'creatureLootTables' contains creature {table.CreatureEntryId} with an out-of-range drop chance for item {item.ItemId}.");
                    }
                }
            }
        }

        return tables;
    }

    public IReadOnlyList<QuestDefinition> LoadQuests()
    {
        var npcIds = LoadNpcs().Select(npc => npc.EntryId).ToHashSet();
        var creatureIds = LoadCreatures().Select(creature => creature.EntryId).ToHashSet();
        var staticObjectEntries = LoadStaticObjectTypes().Select(t => t.Entry).ToHashSet();
        var itemIds = LoadItems().Select(item => item.Id).ToHashSet();
        var quests = LoadDefinitions<QuestDefinition>("quests", "quests");

        foreach (var quest in quests)
        {
            if (quest.GiverEntryId is uint giverEntryId && !npcIds.Contains(giverEntryId))
            {
                throw new InvalidDataException(
                    $"Dataset 'quests' references missing NPC {quest.GiverEntryId} from quest {quest.Id}.");
            }

            foreach (var objective in quest.Objectives)
            {
                if (objective.Kind == QuestObjectiveKind.Killed
                    && !creatureIds.Contains(objective.ObjectiveCreature))
                {
                    throw new InvalidDataException(
                        $"Dataset 'quests' has quest {quest.Id} objective {objective.Index} " +
                        $"referencing missing creature {objective.ObjectiveCreature}.");
                }

                if (objective.Kind == QuestObjectiveKind.Held && objective.ObjectiveItem == 0)
                {
                    throw new InvalidDataException(
                        $"Dataset 'quests' has quest {quest.Id} objective {objective.Index} " +
                        "held on no item at all.");
                }

                if (objective.Kind == QuestObjectiveKind.Held && itemIds.Count > 0
                    && !itemIds.Contains(objective.ObjectiveItem))
                {
                    throw new InvalidDataException(
                        $"Dataset 'quests' has quest {quest.Id} objective {objective.Index} " +
                        $"referencing missing item {objective.ObjectiveItem}.");
                }

                if (objective.Kind == QuestObjectiveKind.Triggered
                    && objective.ObjectiveObject != 0
                    && !staticObjectEntries.Contains(objective.ObjectiveObject))
                {
                    throw new InvalidDataException(
                        $"Dataset 'quests' has quest {quest.Id} objective {objective.Index} " +
                        $"referencing missing static object {objective.ObjectiveObject}.");
                }

                if (objective.GoalCount <= 0)
                {
                    throw new InvalidDataException(
                        $"Dataset 'quests' has quest {quest.Id} objective {objective.Index} " +
                        "requiring a count that is not positive.");
                }
            }
        }

        return quests;
    }

    public IReadOnlyList<QuestRewardDefinition> LoadQuestRewards()
    {
        var questIds = LoadQuests().Select(quest => quest.Id).ToHashSet();
        var rewards = LoadDefinitions<QuestRewardDefinition>("questRewards", "quest rewards");

        foreach (var reward in rewards)
        {
            if (!questIds.Contains(reward.QuestId))
            {
                throw new InvalidDataException(
                    $"Dataset 'questRewards' references missing quest {reward.QuestId}.");
            }
        }

        return rewards;
    }

    public IReadOnlyList<TeleportStopDefinition> LoadTeleportStops()
    {
        var mapIds = LoadMaps().Select(map => map.Id).ToHashSet();
        var stops = LoadDefinitions<TeleportStopDefinition>("teleportStops", "teleport stops");

        foreach (var stop in stops)
        {
            ValidateMapReference("teleportStops", stop.Id, stop.MapId, mapIds);
            ValidatePosition("teleportStops", stop.Id, stop.X, stop.Y, stop.Z, stop.Yaw);
        }

        return stops;
    }

    public IReadOnlyList<GossipMenuDefinition> LoadGossipMenus()
    {
        var mapIds = LoadMaps().Select(map => map.Id).ToHashSet();
        var menus = LoadDefinitions<GossipMenuDefinition>("gossipMenus", "gossip menus");
        var menuIds = menus.Select(menu => menu.Id).ToHashSet();

        foreach (var menu in menus)
        {
            foreach (var subMenu in menu.SubMenus)
            {
                if (!menuIds.Contains(subMenu))
                {
                    throw new InvalidDataException(
                        $"Dataset 'gossipMenus' references missing menu {subMenu} from menu {menu.Id}.");
                }
            }

            foreach (var teleport in menu.Teleports)
            {
                ValidateMapReference("gossipMenus", menu.Id, teleport.MapId, mapIds);
                ValidatePosition("gossipMenus", menu.Id, teleport.X, teleport.Y, teleport.Z, teleport.Yaw);
                if (teleport.Choice is < 1 or > 6 || teleport.Cost < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset 'gossipMenus' contains menu {menu.Id} selling a teleport from choice {teleport.Choice} for {teleport.Cost}.");
                }
            }

            if (menu.Teleports.Select(teleport => teleport.Choice).Distinct().Count() != menu.Teleports.Count)
            {
                throw new InvalidDataException(
                    $"Dataset 'gossipMenus' contains menu {menu.Id} selling two teleports from one choice.");
            }
        }

        return menus;
    }

    public IReadOnlyList<MapGraveyardDefinition> LoadMapGraveyards()
    {
        var mapIds = LoadMaps().Select(map => map.Id).ToHashSet();
        var graveyards = LoadDefinitions<MapGraveyardDefinition>("mapGraveyards", "map graveyards");

        foreach (var graveyard in graveyards)
        {
            ValidateMapReference("mapGraveyards", graveyard.Id, graveyard.MapId, mapIds);
            ValidatePosition("mapGraveyards", graveyard.Id, graveyard.X, graveyard.Y, graveyard.Z, graveyard.Yaw);
        }

        return graveyards;
    }

    public IReadOnlyList<TravelRouteDefinition> LoadTravelRoutes()
    {
        var stopIds = LoadTeleportStops().Select(stop => stop.Id).ToHashSet();
        var routes = LoadDefinitions<TravelRouteDefinition>("travelRoutes", "travel routes");

        foreach (var route in routes)
        {
            if (!stopIds.Contains(route.FirstStop) || !stopIds.Contains(route.SecondStop))
            {
                throw new InvalidDataException(
                    $"Dataset 'travelRoutes' references a missing stop from route {route.Id}.");
            }

            if (route.FirstStop == route.SecondStop)
            {
                throw new InvalidDataException(
                    $"Dataset 'travelRoutes' contains route {route.Id} joining a stop to itself.");
            }
        }

        return routes;
    }

    public IReadOnlyList<StorageUpgradeDefinition> LoadStorageUpgrades() =>
        LoadDefinitions<StorageUpgradeDefinition>("storageUpgrades", "storage upgrades");

    public IReadOnlyList<StaticObjectTypeDefinition> LoadStaticObjectTypes() =>
        LoadDefinitions<StaticObjectTypeDefinition>("staticObjectTypes", "static object types");

    // Not a keyed dataset: one row per playable body, so it is checked here rather than by id.
    public IReadOnlyList<PlayerLookDefinition> LoadPlayerLooks()
    {
        var path = DatasetPath(PlayerLookDefinition.Dataset);
        using var stream = File.OpenRead(path);
        var looks = JsonSerializer.Deserialize<List<PlayerLookDefinition>>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Dataset '{PlayerLookDefinition.Dataset}' must contain a JSON array.");
        foreach (var look in looks)
        {
            if (!PlayerRaceIds.All.Contains(look.Race) || look.Gender < 0)
                throw new InvalidDataException($"Dataset '{PlayerLookDefinition.Dataset}' names no playable body {look.Race}/{look.Gender}.");
            if (new[] { look.Skins, look.Faces, look.HairStyles, look.HairColours }.Any(options => options is null || options.Any(option => option < 0)))
                throw new InvalidDataException($"Dataset '{PlayerLookDefinition.Dataset}' gives body {look.Race}/{look.Gender} a missing or negative option.");
        }

        if (looks.GroupBy(look => (look.Race, look.Gender)).Any(group => group.Count() > 1))
            throw new InvalidDataException($"Dataset '{PlayerLookDefinition.Dataset}' lists a body twice.");
        return looks;
    }

    public IReadOnlyList<TalentTreeDefinition> LoadTalentTrees()
    {
        var trees = LoadDefinitions<TalentTreeDefinition>("talentTrees", "talent trees");
        var talentIds = new HashSet<uint>();
        foreach (var tree in trees)
        {
            var own = tree.Talents.Select(talent => talent.Id).ToHashSet();
            foreach (var talent in tree.Talents)
            {
                if (!talentIds.Add(talent.Id))
                {
                    throw new InvalidDataException($"Dataset 'talentTrees' contains talent {talent.Id} twice.");
                }

                if (talent.Tier < 1 || talent.Column < 1 || talent.PointsInTreeRequired < 0 || talent.MaxRank < 1)
                {
                    throw new InvalidDataException(
                        $"Dataset 'talentTrees' contains talent {talent.Id} with no place, a negative gate, or no ranks.");
                }

                if (talent.Prerequisites.Any(prerequisite => !own.Contains(prerequisite)))
                {
                    throw new InvalidDataException(
                        $"Dataset 'talentTrees' contains talent {talent.Id} needing a talent off its own tree.");
                }
            }
        }

        return trees;
    }

    private IReadOnlyList<T> LoadDefinitions<T>(string dataset, string label) =>
        (IReadOnlyList<T>)_parsed.GetOrAdd((dataset, typeof(T)), _ => ParseDefinitions<T>(dataset, label));

    private IReadOnlyList<T> ParseDefinitions<T>(string dataset, string label)
    {
        var path = DatasetPath(dataset);
        try
        {
            using var stream = File.OpenRead(path);
            var values = JsonSerializer.Deserialize<List<T>>(stream, JsonOptions);
            if (values is null)
            {
                throw new InvalidDataException($"Dataset '{dataset}' must contain a JSON array.");
            }

            ValidateDefinitions(dataset, label, values);
            return values.ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Dataset '{dataset}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static void ValidateDefinitions<T>(string dataset, string label, IReadOnlyList<T> values)
    {
        var ids = new HashSet<(uint Id, int Part)>();
        foreach (var value in values)
        {
            uint id;
            // What tells two rows of one id apart: a vendor's shop tabs share its id.
            int part = 0;
            string name;
            if (value is MapDefinition map)
            {
                id = map.Id;
                name = map.InternalName;
            }
            else if (value is CreatureDefinition creature)
            {
                id = creature.EntryId;
                name = creature.NameKey;
                if (creature.Level < 0 || creature.MaxHealth < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains creature {id} with a negative level or health.");
                }

                if (creature.MinDamage is not null && creature.MaxDamage is not null
                    && creature.MinDamage > creature.MaxDamage)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains creature {id} with min damage above max.");
                }

                if (creature.RespawnTimeMinSeconds is not null && creature.RespawnTimeMaxSeconds is not null
                    && creature.RespawnTimeMinSeconds > creature.RespawnTimeMaxSeconds)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains creature {id} with a respawn range that runs backwards.");
                }

                if (creature.MoneyDropMin is not null && creature.MoneyDropMax is not null
                    && creature.MoneyDropMin > creature.MoneyDropMax)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains creature {id} with a loot money range that runs backwards.");
                }

                if (creature.FactionType is int faction && !UnitFactionTypes.AllWorld.Contains(faction))
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains creature {id} with faction type {faction}, "
                        + "which is not a known world faction type.");
                }
            }
            else if (value is CreatureSpawnDefinition creatureSpawn)
            {
                id = creatureSpawn.Id;
                name = $"spawn {id}";
            }
            else if (value is StaticObjectSpawnDefinition staticSpawn)
            {
                id = staticSpawn.Id;
                name = $"spawn {id}";
            }
            else if (value is NpcDefinition npc)
            {
                id = npc.EntryId;
                name = $"npc {id}";
            }
            else if (value is VendorStockDefinition stock)
            {
                id = stock.SellerEntryId;
                part = stock.Tab.TabIndex;
                name = $"vendor stock {id}";
                if (stock.Tab.Items.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains vendor stock {id} with no items.");
                }
            }
            else if (value is CreatureLootTableDefinition lootTable)
            {
                id = lootTable.CreatureEntryId;
                name = $"loot table {id}";
                if (lootTable.Groups.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains loot table {id} with no groups.");
                }
            }
            else if (value is QuestDefinition quest)
            {
                id = quest.Id;
                name = $"quest {id}";
                if (quest.MinLevel < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains quest {id} with a negative minimum level.");
                }
            }
            else if (value is QuestRewardDefinition reward)
            {
                id = reward.QuestId;
                name = $"quest reward {id}";
                if (reward.Money < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains quest reward {id} with a negative purse.");
                }

                if (reward.Fixed.Concat(reward.Choices).Any(item => item.Count <= 0))
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains quest reward {id} paying an item no times.");
                }
            }
            else if (value is TeleportStopDefinition stop)
            {
                id = stop.Id;
                name = $"stop {id}";
            }
            else if (value is GossipMenuDefinition gossipMenu)
            {
                id = gossipMenu.Id;
                name = $"gossip menu {id}";
            }
            else if (value is MapGraveyardDefinition graveyard)
            {
                id = graveyard.Id;
                name = $"graveyard {id}";
            }
            else if (value is TravelRouteDefinition route)
            {
                id = route.Id;
                name = $"route {id}";
                if (route.Price < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains route {id} with a negative price.");
                }
            }
            else if (value is SpellDefinition spell)
            {
                id = spell.Id;
                name = spell.NameKey;
                if (spell.CastTimeMs < 0 || spell.CooldownMs < 0)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains spell {id} with a negative cast time or cooldown.");
                }
            }
            else if (value is ItemDefinition item)
            {
                id = item.Id;
                name = item.NameKey;
            }
            else if (value is StaticObjectTypeDefinition staticType)
            {
                id = staticType.Entry;
                name = $"static object type {id}";
            }
            else if (value is TalentTreeDefinition talentTree)
            {
                id = talentTree.Id;
                name = $"talent tree {id}";
                if (talentTree.Talents is not { Count: > 0 })
                {
                    throw new InvalidDataException($"Dataset '{dataset}' contains talent tree {id} with no talents.");
                }
            }
            else if (value is StorageUpgradeDefinition upgrade)
            {
                id = upgrade.Id;
                name = $"storage upgrade {id}";
                if (upgrade.Area is not ("bag" or "bank"))
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains storage upgrade {id} with an unrecognized area '{upgrade.Area}'.");
                }

                if (upgrade.SlotCount <= upgrade.FromSlotCount)
                {
                    throw new InvalidDataException(
                        $"Dataset '{dataset}' contains storage upgrade {id} that does not raise capacity.");
                }
            }
            else
            {
                throw new InvalidOperationException($"No validator exists for {label} definitions.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Dataset '{dataset}' contains definition {id} with no name.");
            }

            if (!ids.Add((id, part)))
            {
                throw new InvalidDataException(part == 0
                    ? $"Dataset '{dataset}' contains duplicate id {id}."
                    : $"Dataset '{dataset}' contains duplicate id {id} (tab {part}).");
            }
        }
    }

    private static void ValidateMapReference(
        string dataset,
        uint id,
        ushort mapId,
        IReadOnlySet<ushort> mapIds)
    {
        if (!mapIds.Contains(mapId))
        {
            throw new InvalidDataException($"Dataset '{dataset}' references missing map {mapId} from spawn {id}.");
        }
    }

    private static void ValidatePosition(string dataset, uint id, float x, float y, float z, float yaw)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
        {
            throw new InvalidDataException($"Dataset '{dataset}' contains non-finite position data for spawn {id}.");
        }
    }

    private bool TryResolveDatasetPath(string file, out string path, out string? error)
    {
        path = string.Empty;
        error = null;

        if (Path.IsPathRooted(file) || file.Contains(':', StringComparison.Ordinal))
        {
            error = "must use a relative path.";
            return false;
        }

        var root = Path.GetFullPath(Directory!);
        var candidate = Path.GetFullPath(Path.Combine(root, file));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = "path escapes the data directory.";
            return false;
        }

        path = candidate;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
