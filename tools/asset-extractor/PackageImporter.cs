using System.Security.Cryptography;
using System.Text.Json;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class PackageImporter
{
    public sealed record ImportResult(
        int MapCount, int CreatureCount, int SpawnCount, int DroppedSpawnCount, int SpellCount, int ItemCount, int DroppedItemCount,
        int ZoneCount, int DroppedZoneCount, int QuestCount, int DroppedQuestCount, int NpcCount,
        int QuestRewardCount, int TeleportStopCount, int DroppedTeleportStopCount, int TravelRouteCount,
        int DroppedTravelRouteCount, int StaticObjectCount, int DroppedStaticObjectCount,
        int StorageUpgradeCount, int StaticObjectTypeCount, int VendorStockCount,
        int StaticObjectQuestStarterCount, int StaticObjectQuestFinisherCount,
        int StaticObjectQuestItemBindingCount, int CreatureQuestFinisherCount,
        int CreatureLootTableCount,
        int MapGraveyardCount, int DroppedMapGraveyardCount,
        int GossipMenuCount, int DroppedGossipTeleportCount,
        int TalentTreeCount);

    public static ImportResult ImportBuild114(
        IReadOnlyDictionary<string, byte[]> archiveEntries, string outDirectory, string sourceHash,
        IReadOnlyDictionary<string, byte[]>? worldArchives = null)
    {
        FTableDocument Load(string name)
        {
            if (!archiveEntries.TryGetValue(name, out var bytes))
            {
                throw new FTableFormatException($"Archive is missing required table '{name}'.");
            }

            return FTableDocument.Load(bytes);
        }

        FTableDocument? TryLoad(string name) =>
            archiveEntries.TryGetValue(name, out var bytes) ? FTableDocument.Load(bytes) : null;

        var mapInfoDocument = Load("tables/map_info.tbl");
        var maps = MapTableMapper.MapMaps(mapInfoDocument, MapInfoColumns.For(mapInfoDocument.Header));
        var archiveByMapId = new Dictionary<ushort, byte[]>();
        if (worldArchives is { Count: > 0 })
        {
            maps = maps.Select(map =>
            {
                var stem = map.File is not null
                    ? Path.GetFileNameWithoutExtension(map.File)
                    : null;
                if (stem is null || !worldArchives.TryGetValue(stem, out var archiveBytes)) return map;

                archiveByMapId[map.Id] = archiveBytes;

                using var stream = new MemoryStream(archiveBytes);
                var bounds = WorldBoundsReader.ComputeBoundsFromArchive(stream);
                return bounds is null
                    ? map
                    : map with { MinX = bounds.Value.MinX, MaxX = bounds.Value.MaxX, MinZ = bounds.Value.MinZ, MaxZ = bounds.Value.MaxZ };
            }).ToList();
        }

        var creatureNameStringIds = CreatureAppearanceTableMapper.MapNameStringIds(
            Load("tables/creature_appearance.tbl"), new CreatureAppearanceTableColumns(Entry: 0, NameTextId: 1));
        var creatureStrings = StringTableMapper.MapStrings(Load("tables/String_Creature_en.tbl"));
        var creatureProtoDocument = Load("tables/creature_proto.tbl");
        var creatures = CreatureTableMapper.MapCreatures(creatureProtoDocument, CreatureProtoColumns.Build114,
            resolveName: entry => creatureNameStringIds.TryGetValue(entry, out var stringId)
                && creatureStrings.TryGetValue(stringId, out var text)
                && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"creature-{entry}");

        var mapIds = new HashSet<ushort>(maps.Select(m => m.Id));
        var creatureEntries = new HashSet<uint>(creatures.Select(c => c.EntryId));
        var spawnDocument = Load("tables/creature_spawns.tbl");
        var knownSpawnRows = spawnDocument.Rows.Where(row =>
            row.Count > 2 && row[1] is uint entry && creatureEntries.Contains(entry)
            && row[2] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedSpawns = spawnDocument.Rows.Count - knownSpawnRows.Count;
        var spawns = CreatureSpawnTableMapper.MapSpawns(
            spawnDocument.WithRows(knownSpawnRows), CreatureSpawnColumns.Build114, mapIds, creatureEntries);

        var spellStrings = StringTableMapper.MapStrings(Load("tables/String_Spell_en.tbl"));
        var spellNewDocument = Load("tables/spell_new.tbl");
        var spells = SpellTableMapper.MapSpells(spellNewDocument, SpellNewColumns.Build114,
            resolveName: id => spellStrings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"spell-{id}");

        var spellRowsById = spellNewDocument.Rows.ToDictionary(row => (uint)row[0]);
        var spellEffectsById = SpellEffectTableMapper.MapEffects(
            Load("tables/spell_effect.tbl"), SpellEffectColumns.Build114).ToDictionary(e => e.Id);
        spells = spells.Select(spell =>
        {
            if (!spellRowsById.TryGetValue(spell.Id, out var row)) return spell;
            int? powerCost = row[41] is uint pc && pc > 0 ? (int)pc : null;
            uint[]? groupMask = row[44] is uint g0 && row[45] is uint g1 && row[46] is uint g2
                ? [g0, g1, g2] : null;
            var slots = new[] { 33, 34, 35, 36, 37 }
                .Select(column => (column, row.Count > column && row[column] is uint effectId && effectId != 0
                    && spellEffectsById.TryGetValue(effectId, out var found) ? found : null))
                .ToList();
            return SpellEffectClassifier.ApplySlots(
                spell with { PowerCost = powerCost, SpellGroupMask = groupMask }, slots);
        }).ToList();

        var itemStrings = StringTableMapper.MapStrings(Load("tables/String_Item_en.tbl"));
        var itemDocument = Load("tables/items.tbl");
        // 4.2.2 gives 68 class 15 rows, open to every class and race, the subclass 9999.
        var knownItemRows = itemDocument.Rows.Where(row =>
            row[ItemColumns.Build114.Class] is uint itemClass && itemClass <= byte.MaxValue
            && row[ItemColumns.Build114.SubClass] is uint subClass && subClass <= byte.MaxValue).ToList();
        int droppedItems = itemDocument.Rows.Count - knownItemRows.Count;
        itemDocument = itemDocument.WithRows(knownItemRows);
        var items = ItemTableMapper.MapItems(itemDocument, ItemColumns.Build114,
            resolveName: id => itemStrings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"item-{id}");

        var itemMetaByEntry = new Dictionary<uint, (uint NameTextId, int Quality)>();
        foreach (var row in itemDocument.Rows)
        {
            if (row[0] is uint entry && row[3] is uint nameId && row[5] is uint quality)
            {
                itemMetaByEntry.TryAdd(entry, (nameId, unchecked((int)quality)));
            }
        }

        var sellListsByCreature = new Dictionary<uint, IReadOnlyList<uint>>();
        foreach (var row in Load("tables/creature_proto.tbl").Rows)
        {
            if (row.Count > 55 && row[0] is uint creatureEntry && row[55] is string listText
                && SellListIds.Parse(listText) is { Count: > 0 } lists)
            {
                sellListsByCreature[creatureEntry] = lists;
            }
        }

        var sellProgramCurrency = new Dictionary<uint, uint>();
        foreach (var row in Load("tables/creature_machant_sell_program.tbl").Rows)
        {
            if (row[0] is uint vendorListId && row[2] is uint currencyId)
            {
                sellProgramCurrency.TryAdd(vendorListId, currencyId);
            }
        }

        var sellProgramItems = new Dictionary<uint, List<(uint ItemId, int StackCount, int MaxCount)>>();
        foreach (var row in Load("tables/creature_machant_sell_program_item.tbl").Rows)
        {
            if (row[1] is uint vendorListId && row[2] is uint itemId
                && row[3] is uint stackCount && row[4] is uint maxCount)
            {
                if (!sellProgramItems.TryGetValue(vendorListId, out var programItems))
                {
                    sellProgramItems[vendorListId] = programItems = [];
                }

                programItems.Add((itemId, unchecked((int)stackCount), unchecked((int)maxCount)));
            }
        }

        var itemsByEntry = items.ToDictionary(i => i.Id);

        var creaturesByEntry = creatures.ToDictionary(c => c.EntryId);
        // Later builds (4.2.2) carry no loot tables; the dataset is then left out.
        var lootFeatureItems = TryLoad("tables/loot_feature_items.tbl");
        var lootFeature = TryLoad("tables/loot_feature.tbl");
        var loot = TryLoad("tables/loot.tbl");
        var leftOut = new HashSet<string>();
        if (lootFeatureItems is null || lootFeature is null || loot is null) leftOut.Add("creatureLootTables");
        var lootGroupsByCreature = new Dictionary<uint, List<LootGroupDefinition>>();
        if (lootFeatureItems is not null && lootFeature is not null && loot is not null)
        {
            var lootItemsByGroup = new Dictionary<uint, List<LootTableItemDefinition>>();
            foreach (var row in lootFeatureItems.Rows)
            {
                if (row[1] is not uint groupId || row[2] is not uint itemId || row[3] is not float chance
                    || row[4] is not uint fewest || row[5] is not uint most)
                {
                    continue;
                }

                if (!itemsByEntry.ContainsKey(itemId) || chance <= 0 || fewest == 0 || fewest > most)
                {
                    continue;
                }

                if (!lootItemsByGroup.TryGetValue(groupId, out var candidateItems))
                {
                    lootItemsByGroup[groupId] = candidateItems = [];
                }

                candidateItems.Add(new LootTableItemDefinition(itemId, chance, unchecked((int)fewest), unchecked((int)most)));
            }

            var lootGroupsById = new Dictionary<uint, (int MinLevel, int MaxLevel, int GuaranteedDrops, int MaxDrops)>();
            foreach (var row in lootFeature.Rows)
            {
                if (row[0] is not uint groupId || row[2] is not uint lowestLevel || row[3] is not uint highestLevel
                    || row[6] is not uint guaranteed || row[7] is not uint upTo)
                {
                    continue;
                }

                lootGroupsById[groupId] = (
                    unchecked((int)lowestLevel), unchecked((int)highestLevel),
                    unchecked((int)guaranteed), unchecked((int)upTo));
            }

            const uint CreatureLootTargetType = 1;
            foreach (var row in loot.Rows)
            {
                if (row[1] is not uint targetId || row[2] is not uint targetKind || targetKind != CreatureLootTargetType
                    || row[3] is not uint groupId)
                {
                    continue;
                }

                if (!creaturesByEntry.TryGetValue(targetId, out var creature)) continue;
                if (!lootGroupsById.TryGetValue(groupId, out var group)) continue;
                if (creature.Level < group.MinLevel || creature.Level > group.MaxLevel) continue;
                if (!lootItemsByGroup.TryGetValue(groupId, out var candidates) || candidates.Count == 0) continue;

                if (!lootGroupsByCreature.TryGetValue(targetId, out var creatureGroups))
                {
                    lootGroupsByCreature[targetId] = creatureGroups = [];
                }

                creatureGroups.Add(new LootGroupDefinition(group.GuaranteedDrops, group.MaxDrops, candidates));
            }
        }

        var creatureLootTables = lootGroupsByCreature
            .Select(kv => new CreatureLootTableDefinition(kv.Key, kv.Value))
            .ToList();

        var zoneStrings = StringTableMapper.MapStrings(Load("tables/String_Zones_en.tbl"));
        var zoneDocument = Load("tables/zones.tbl");
        var knownZoneRows = zoneDocument.Rows.Where(row =>
            row.Count > 10 && row[3] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)
            && row[0] is int id && zoneStrings.ContainsKey(unchecked((uint)id))
            && row[4] is float minX && row[8] is float maxX && minX <= maxX
            && row[6] is float minZ && row[10] is float maxZ && minZ <= maxZ).ToList();
        int droppedZones = zoneDocument.Rows.Count - knownZoneRows.Count;
        var zones = ZoneTableMapper.MapZones(
            zoneDocument.WithRows(knownZoneRows), ZoneColumns.Build114, mapIds,
            resolveName: id => zoneStrings[id]);

        var giversByQuest = new Dictionary<uint, uint>();
        foreach (var row in Load("tables/creature_quest_starter.tbl").Rows)
        {
            if (row[0] is uint creatureId && row[1] is uint questId) giversByQuest.TryAdd(questId, creatureId);
        }

        var creatureQuestFinishers = Load("tables/creature_quest_finisher.tbl").Rows
            .Where(row => row[0] is uint entry && creatureEntries.Contains(entry) && row[1] is uint)
            .Select(row => new CreatureQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();

        var itemStarterQuestIds = items.Where(i => i.QuestId is not null)
            .Select(i => i.QuestId!.Value).ToHashSet();

        var staticObjectEntries = Load("tables/staticobject_proto.tbl").Rows
            .Where(row => row[0] is uint)
            .Select(row => (uint)row[0]).ToHashSet();
        var staticObjectQuestStarters = Load("tables/staticobject_quest_starter.tbl").Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry) && row[1] is uint)
            .Select(row => new StaticObjectQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();
        var staticObjectQuestFinishers = Load("tables/staticobject_quest_finisher.tbl").Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry) && row[1] is uint)
            .Select(row => new StaticObjectQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();
        var staticObjectStarterQuestIds = staticObjectQuestStarters.Select(b => b.QuestId).ToHashSet();
        var staticObjectFinisherQuestIds = staticObjectQuestFinishers.Select(b => b.QuestId).ToHashSet();

        var staticObjectQuestItemBindings = Load("tables/staticobject_quest_item_binding.tbl").Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry)
                && row[1] is uint && row[2] is uint && row[3] is uint)
            .Select(row => new StaticObjectItemBindingDefinition((uint)row[0], (uint)row[1], (uint)row[2], (uint)row[3]))
            .ToList();

        var questDocument = Load("tables/quests.tbl");
        var questsWithKnownGivers = questDocument.Rows.Where(row =>
            row[0] is uint id
            && ((giversByQuest.TryGetValue(id, out var giver) && creatureEntries.Contains(giver))
                || itemStarterQuestIds.Contains(id)
                || staticObjectStarterQuestIds.Contains(id)
                || staticObjectFinisherQuestIds.Contains(id))).ToList();
        int droppedQuests = questDocument.Rows.Count - questsWithKnownGivers.Count;

        var questGivers = giversByQuest.Values.Where(creatureEntries.Contains).ToHashSet();

        const int GreetingMenuColumn = 56;
        var greetingByEntry = new Dictionary<uint, uint>();
        foreach (var row in creatureProtoDocument.Rows)
        {
            if (row.Count > GreetingMenuColumn && row[0] is uint entry && row[GreetingMenuColumn] is uint menu && menu != 0)
                greetingByEntry.TryAdd(entry, menu);
        }

        var npcs = NpcOverlays.Build(creatures, questGivers, greetingByEntry);
        var npcFlagsByEntry = npcs.ToDictionary(npc => npc.EntryId, npc => npc.Flags);

        var vendorStock = new List<VendorStockDefinition>();
        foreach (var (creatureEntry, vendorListIds) in sellListsByCreature)
        {
            if (!npcFlagsByEntry.TryGetValue(creatureEntry, out var npcFlags) || !npcFlags.HasFlag(NpcFlags.Vendor))
            {
                continue;
            }

            var tabIndex = 0;
            foreach (var vendorListId in vendorListIds)
            {
                if (!sellProgramItems.TryGetValue(vendorListId, out var programItems) || programItems.Count == 0) continue;

                var stockItems = new List<VendorItem>();
                for (var slot = 0; slot < programItems.Count; slot++)
                {
                    var (itemId, stackCount, maxCount) = programItems[slot];
                    if (!itemsByEntry.TryGetValue(itemId, out var item)) continue;
                    if (!itemMetaByEntry.TryGetValue(itemId, out var meta)) continue;

                    stockItems.Add(new VendorItem(
                        slot, unchecked((int)itemId), unchecked((int)meta.NameTextId),
                        unchecked((int)item.DisplayInfoId), unchecked((int)(item.BuyPrice ?? 0)),
                        maxCount, stackCount));
                }

                if (stockItems.Count == 0) continue;

                var currencyKind = unchecked((int)sellProgramCurrency.GetValueOrDefault(vendorListId));
                vendorStock.Add(new VendorStockDefinition(
                    creatureEntry, new VendorTab(tabIndex++, currencyKind, stockItems.Count, true, stockItems)));
            }
        }

        var nonCreatureStarterQuestIds = itemStarterQuestIds
            .Union(staticObjectStarterQuestIds).Union(staticObjectFinisherQuestIds).ToHashSet();
        var mappedQuests = QuestTableMapper.MapQuests(
            questDocument.WithRows(questsWithKnownGivers), QuestColumns.Build114,
            npcEntries: npcs.Select(n => n.EntryId).ToHashSet(),
            resolveGiverEntry: id => giversByQuest.TryGetValue(id, out var giver) ? giver : null,
            nonCreatureStarterQuestIds: nonCreatureStarterQuestIds);
        // 4.2.2 gives a few objectives a count of 0, which no player could finish
        // against; such a quest is left out rather than guessed at.
        var quests = mappedQuests.Where(quest => quest.Objectives.All(objective => objective.GoalCount > 0)).ToList();
        droppedQuests += mappedQuests.Count - quests.Count;

        var questIds = new HashSet<uint>(quests.Select(q => q.Id));
        var questRewards = QuestRewardTableMapper.MapRewards(
            questDocument.WithRows(questDocument.Rows.Where(row => row[0] is uint id && questIds.Contains(id))),
            QuestRewardColumns.Build114, questIds,
            resolveItem: entry => itemMetaByEntry.TryGetValue(entry, out var meta) ? meta : null);

        var teleportDocument = Load("tables/teleport_spot.tbl");
        var knownTeleportRows = teleportDocument.Rows.Where(row =>
            row[2] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedTeleportStops = teleportDocument.Rows.Count - knownTeleportRows.Count;
        var teleportStops = TeleportStopTableMapper.MapTeleportStops(
            teleportDocument.WithRows(knownTeleportRows), TeleportStopColumns.Build114, mapIds);

        var stopIds = new HashSet<uint>(teleportStops.Select(s => s.Id));
        var travelRouteDocument = Load("tables/t_bus_route.tbl");
        var knownTravelRouteRows = travelRouteDocument.Rows.Where(row =>
            row[1] is uint from && stopIds.Contains(from)
            && row[2] is uint to && stopIds.Contains(to) && from != to).ToList();
        int droppedTravelRoutes = travelRouteDocument.Rows.Count - knownTravelRouteRows.Count;
        var travelRoutes = TravelRouteTableMapper.MapTravelRoutes(
            travelRouteDocument.WithRows(knownTravelRouteRows), TravelRouteColumns.Build114, stopIds);

        var staticObjectDocument = Load("tables/staticobject_spawns.tbl");
        var knownStaticObjectRows = staticObjectDocument.Rows.Where(row =>
            row[2] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedStaticObjects = staticObjectDocument.Rows.Count - knownStaticObjectRows.Count;
        var staticObjects = StaticObjectTableMapper.MapStaticObjects(
            staticObjectDocument.WithRows(knownStaticObjectRows), StaticObjectColumns.Build114, mapIds);

        var graveyardDocument = Load("tables/map_graveyard.tbl");
        var knownGraveyardRows = graveyardDocument.Rows.Where(row =>
            row[6] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedGraveyards = graveyardDocument.Rows.Count - knownGraveyardRows.Count;
        var graveyards = MapGraveyardTableMapper.MapGraveyards(
            graveyardDocument.WithRows(knownGraveyardRows), MapGraveyardColumns.Build114, mapIds);

        var gossip = GossipMenuTableMapper.MapMenus(
            Load("tables/chat_menu.tbl"),
            StringTableMapper.MapStrings(Load("tables/String_Menu_en.tbl")),
            zones.ToDictionary(zone => zone.Id, zone => zone.MapId));
        var gossipMenus = gossip.Menus;

        // 4.2.2 adds a column whose meaning is not settled, so that table stays out.
        var storageUpgradeDocument = Load("tables/storage_upgrade.tbl");
        var storageUpgrades = storageUpgradeDocument.Header.Cols == StorageUpgradeColumns.Build114Cols
            ? StorageUpgradeTableMapper.MapUpgrades(storageUpgradeDocument, StorageUpgradeColumns.Build114)
            : [];
        if (storageUpgradeDocument.Header.Cols != StorageUpgradeColumns.Build114Cols) leftOut.Add("storageUpgrades");

        var staticObjectTypes = StaticObjectProtoTableMapper.MapTypes(
            Load("tables/staticobject_proto.tbl"), StaticObjectProtoColumns.Build114);

        var talentDocument = Load("tables/spell_talent.tbl");
        var talentTrees = TalentTableMapper.MapTrees(
            talentDocument, Load("tables/character_class.tbl"),
            TalentColumns.For(talentDocument.Header), TalentColumns.ClassBuild114);

        Directory.CreateDirectory(outDirectory);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        void Write<T>(string dataset, T value)
        {
            if (!leftOut.Contains(dataset))
                File.WriteAllText(Path.Combine(outDirectory, dataset + ".json"), JsonSerializer.Serialize(value, options));
        }

        Write("maps", maps);
        Write("creatures", creatures);
        Write("creatureSpawns", spawns);
        Write("spells", spells);
        Write("items", items);
        Write("quests", quests);
        Write("questRewards", questRewards);
        Write("npcs", npcs);
        Write("vendorStock", vendorStock);
        Write("staticObjectQuestStarters", staticObjectQuestStarters);
        Write("staticObjectQuestFinishers", staticObjectQuestFinishers);
        Write("staticObjectQuestItemBindings", staticObjectQuestItemBindings);
        Write("creatureQuestFinishers", creatureQuestFinishers);
        Write("creatureLootTables", creatureLootTables);
        Write("teleportStops", teleportStops);
        Write("gossipMenus", gossipMenus);
        Write("mapGraveyards", graveyards);
        Write("travelRoutes", travelRoutes);
        Write("staticObjectSpawns", staticObjects);
        Write("storageUpgrades", storageUpgrades);
        Write("staticObjectTypes", staticObjectTypes);
        Write("talentTrees", talentTrees);
        WriteTerrain(archiveByMapId, outDirectory);

        var manifest = new DataManifest(
            ExtractorVersion: "0.1.0",
            GameVersion: MapInfoColumns.BuildOf(mapInfoDocument.Header),
            SourceHash: sourceHash,
            GeneratedAt: DateTimeOffset.UtcNow,
            Datasets: new Dictionary<string, string>
            {
                ["maps"] = "maps.json",
                ["spells"] = "spells.json",
                ["creatures"] = "creatures.json",
                ["creatureSpawns"] = "creatureSpawns.json",
                ["items"] = "items.json",
                ["quests"] = "quests.json",
                ["questRewards"] = "questRewards.json",
                ["npcs"] = "npcs.json",
                ["vendorStock"] = "vendorStock.json",
                ["staticObjectQuestStarters"] = "staticObjectQuestStarters.json",
                ["staticObjectQuestFinishers"] = "staticObjectQuestFinishers.json",
                ["staticObjectQuestItemBindings"] = "staticObjectQuestItemBindings.json",
                ["creatureQuestFinishers"] = "creatureQuestFinishers.json",
                ["creatureLootTables"] = "creatureLootTables.json",
                ["teleportStops"] = "teleportStops.json",
                ["gossipMenus"] = "gossipMenus.json",
                ["mapGraveyards"] = "mapGraveyards.json",
                ["travelRoutes"] = "travelRoutes.json",
                ["staticObjectSpawns"] = "staticObjectSpawns.json",
                ["storageUpgrades"] = "storageUpgrades.json",
                ["staticObjectTypes"] = "staticObjectTypes.json",
                ["talentTrees"] = "talentTrees.json",
            }.Where(dataset => !leftOut.Contains(dataset.Key))
                .ToDictionary(dataset => dataset.Key, dataset => dataset.Value));
        DataManifestJson.Save(manifest, Path.Combine(outDirectory, DataManifest.FileName));

        return new ImportResult(
            maps.Count, creatures.Count, spawns.Count, droppedSpawns, spells.Count, items.Count, droppedItems,
            zones.Count, droppedZones, quests.Count, droppedQuests, npcs.Count, questRewards.Count,
            teleportStops.Count, droppedTeleportStops, travelRoutes.Count, droppedTravelRoutes,
            staticObjects.Count, droppedStaticObjects, storageUpgrades.Count, staticObjectTypes.Count,
            vendorStock.Count, staticObjectQuestStarters.Count, staticObjectQuestFinishers.Count,
            staticObjectQuestItemBindings.Count, creatureQuestFinishers.Count,
            creatureLootTables.Count,
            graveyards.Count, droppedGraveyards,
            gossipMenus.Count, gossip.DroppedTeleports,
            talentTrees.Count);
    }

    private static void WriteTerrain(IReadOnlyDictionary<ushort, byte[]> archiveByMapId, string outDirectory)
    {
        if (archiveByMapId.Count == 0) return;

        var terrainDirectory = Path.Combine(outDirectory, TerrainPackage.DirectoryName);
        Directory.CreateDirectory(terrainDirectory);
        foreach (var (mapId, archiveBytes) in archiveByMapId)
        {
            using var source = new MemoryStream(archiveBytes);
            var tiles = TerrainTileReader.ReadTiles(source);
            if (tiles.Count == 0) continue;

            using var file = File.Create(Path.Combine(terrainDirectory, TerrainPackage.FileName(mapId)));
            TerrainPackage.Write(file, mapId, tiles);
        }
    }

    public static string HashArchive(byte[] archiveBytes) =>
        Convert.ToHexString(SHA256.HashData(archiveBytes));
}
