using System.Security.Cryptography;
using System.Text.Json;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class PackageImporter
{
    public sealed record ImportResult(
        int MapCount, int CreatureCount, int SpawnCount, int DroppedSpawnCount, int SpellCount, int ItemCount,
        int ZoneCount, int DroppedZoneCount, int QuestCount, int DroppedQuestCount, int NpcCount,
        int QuestRewardCount, int TeleportStopCount, int DroppedTeleportStopCount, int TravelRouteCount,
        int DroppedTravelRouteCount, int StaticObjectCount, int DroppedStaticObjectCount,
        int StorageUpgradeCount, int StaticObjectTypeCount, int VendorStockCount,
        int StaticObjectQuestStarterCount, int StaticObjectQuestFinisherCount,
        int StaticObjectQuestItemBindingCount, int CreatureQuestFinisherCount,
        int CreatureLootTableCount,
        int MapGraveyardCount, int DroppedMapGraveyardCount,
        int GossipMenuCount, int DroppedGossipTeleportCount,
        int TalentTreeCount,
        int DungeonRewardCount,
        string Build,
        string SpellEffectSlotColumns);

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

        FTableDocument? LoadOptional(string name) =>
            archiveEntries.TryGetValue(name, out var bytes) ? FTableDocument.Load(bytes) : null;

        string build = TableSchemas.IdentifyBuild(TableFingerprint.Capture(archiveEntries, sourceHash));
        var mapDocument = Load("tables/map_info.tbl");
        var maps = MapTableMapper.MapMaps(mapDocument, MapInfoColumns.Resolve(mapDocument));
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

        var creatureAppearanceDocument = Load("tables/creature_appearance.tbl");
        var creatureNameStringIds = CreatureAppearanceTableMapper.MapNameStringIds(
            creatureAppearanceDocument, CreatureAppearanceTableMapper.Resolve(creatureAppearanceDocument));
        var creatureStrings = StringTableMapper.MapStrings(Load("tables/String_Creature_en.tbl"));
        var creatureProtoDocument = Load("tables/creature_proto.tbl");
        var creatureProtoColumns = CreatureProtoColumns.Resolve(creatureProtoDocument);
        var creatures = CreatureTableMapper.MapCreatures(creatureProtoDocument, creatureProtoColumns,
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
            spawnDocument.WithRows(knownSpawnRows), CreatureSpawnColumns.Resolve(spawnDocument), mapIds, creatureEntries);

        var spellEffectDocument = Load("tables/spell_effect.tbl");
        var spellEffectColumns = SpellEffectColumns.Resolve(spellEffectDocument);
        var spellEffectsById = SpellEffectTableMapper.MapEffects(spellEffectDocument, spellEffectColumns)
            .ToDictionary(e => e.Id);
        var spellStrings = StringTableMapper.MapStrings(Load("tables/String_Spell_en.tbl"));
        var spellNewDocument = Load("tables/spell_new.tbl");
        var (spellNewColumns, spellNotes) = SpellNewColumns.Resolve(
            spellNewDocument, new HashSet<uint>(spellEffectsById.Keys));
        foreach (var note in spellNotes) Console.WriteLine($"  note: {note}");
        var spells = SpellTableMapper.MapSpells(spellNewDocument, spellNewColumns,
            resolveName: id => spellStrings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"spell-{id}");

        var spellRowsById = spellNewDocument.Rows.ToDictionary(row => (uint)row[0]);
        var effectSlotOrder = (spellNewColumns.EffectSlots ?? []).ToList();
        spells = spells.Select(spell =>
        {
            if (!spellRowsById.TryGetValue(spell.Id, out var row)) return spell;
            int? powerCost = spellNewColumns.PowerCost >= 0
                && row.Count > spellNewColumns.PowerCost
                && row[spellNewColumns.PowerCost] is uint pc && pc > 0 ? (int)pc : null;
            uint[]? groupMask = spellNewColumns.GroupMaskColumns is { Count: 3 } mask
                && mask.All(column => row.Count > column && row[column] is uint)
                ? [.. mask.Select(column => (uint)row[column])] : null;
            var slots = effectSlotOrder
                .Select(column => (column, row.Count > column && row[column] is uint effectId && effectId != 0
                    && spellEffectsById.TryGetValue(effectId, out var found) ? found : null))
                .ToList();
            return SpellEffectClassifier.ApplySlots(
                spell with { PowerCost = powerCost, SpellGroupMask = groupMask }, slots, effectSlotOrder);
        }).ToList();

        var itemStrings = StringTableMapper.MapStrings(Load("tables/String_Item_en.tbl"));
        var itemDocument = Load("tables/items.tbl");
        var items = ItemTableMapper.MapItems(itemDocument, ItemColumns.Resolve(itemDocument),
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
        if (creatureProtoColumns.SellList >= 0)
        {
            int sellListColumn = creatureProtoColumns.SellList;
            foreach (var row in creatureProtoDocument.Rows)
            {
                if (row.Count > sellListColumn && row[0] is uint creatureEntry && row[sellListColumn] is string listText
                    && SellListIds.Parse(listText) is { Count: > 0 } lists)
                {
                    sellListsByCreature[creatureEntry] = lists;
                }
            }
        }
        else
        {
            Console.WriteLine($"  note: {build} has no readable vendor-list column; vendor stock skipped.");
        }

        var sellProgramDocument = Load("tables/creature_machant_sell_program.tbl");
        TableShape.Expect("tables/creature_machant_sell_program.tbl", sellProgramDocument.Header.ColumnTypes,
            (0, "uk"), (2, "u"));
        var sellProgramCurrency = new Dictionary<uint, uint>();
        foreach (var row in sellProgramDocument.Rows)
        {
            if (row[0] is uint vendorListId && row[2] is uint currencyId)
            {
                sellProgramCurrency.TryAdd(vendorListId, currencyId);
            }
        }

        var sellProgramItemDocument = Load("tables/creature_machant_sell_program_item.tbl");
        TableShape.Expect("tables/creature_machant_sell_program_item.tbl", sellProgramItemDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"), (2, "u"), (3, "u"), (4, "u"));
        var sellProgramItems = new Dictionary<uint, List<(uint ItemId, int StackCount, int MaxCount)>>();
        foreach (var row in sellProgramItemDocument.Rows)
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
        // A build can drop the loot tables entirely (2.2.0 moves loot to
        // dungeon rewards): then creatures simply drop nothing, loudly noted.
        var lootItemsDocument = LoadOptional("tables/loot_feature_items.tbl");
        var lootFeatureDocument = lootItemsDocument is null ? null : LoadOptional("tables/loot_feature.tbl");
        var lootDocument = lootFeatureDocument is null ? null : LoadOptional("tables/loot.tbl");
        if (lootDocument is null)
        {
            Console.WriteLine($"  note: {build} has no loot tables; creatures drop nothing " +
                "(dungeon rewards import to dungeonRewards.json but nothing awards them yet).");
        }

        if (lootItemsDocument is not null)
        {
            TableShape.Expect("tables/loot_feature_items.tbl", lootItemsDocument.Header.ColumnTypes,
                (1, "u"), (2, "u"), (3, "f"), (4, "u"), (5, "u"));
        }

        var lootItemsByGroup = new Dictionary<uint, List<LootTableItemDefinition>>();
        foreach (var row in lootItemsDocument?.Rows ?? [])
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

        if (lootFeatureDocument is not null)
        {
            TableShape.Expect("tables/loot_feature.tbl", lootFeatureDocument.Header.ColumnTypes,
                (0, "uk"), (2, "u"), (3, "u"), (6, "u"), (7, "u"));
        }

        var lootGroupsById = new Dictionary<uint, (int MinLevel, int MaxLevel, int GuaranteedDrops, int MaxDrops)>();
        foreach (var row in lootFeatureDocument?.Rows ?? [])
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
        if (lootDocument is not null)
        {
            TableShape.Expect("tables/loot.tbl", lootDocument.Header.ColumnTypes,
                (1, "u"), (2, "u"), (3, "u"));
        }

        var lootGroupsByCreature = new Dictionary<uint, List<LootGroupDefinition>>();
        foreach (var row in lootDocument?.Rows ?? [])
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

        var creatureLootTables = lootGroupsByCreature
            .Select(kv => new CreatureLootTableDefinition(kv.Key, kv.Value))
            .ToList();

        var dungeonRewardDocument = LoadOptional("tables/dungeon_reward.tbl");
        var dungeonRewards = new List<DungeonRewardDefinition>();
        if (dungeonRewardDocument is not null)
        {
            dungeonRewards.AddRange(DungeonRewardTableMapper.MapRewards(
                dungeonRewardDocument, DungeonRewardColumns.Resolve(dungeonRewardDocument),
                mapIds, new HashSet<uint>(itemsByEntry.Keys)));
        }

        var zoneStrings = StringTableMapper.MapStrings(Load("tables/String_Zones_en.tbl"));
        var zoneDocument = Load("tables/zones.tbl");
        var zoneColumns = ZoneColumns.Resolve(zoneDocument);
        var knownZoneRows = zoneDocument.Rows.Where(row =>
            row.Count > 10 && row[3] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)
            && row[0] is int id && zoneStrings.ContainsKey(unchecked((uint)id))
            && row[4] is float minX && row[8] is float maxX && minX <= maxX
            && row[6] is float minZ && row[10] is float maxZ && minZ <= maxZ).ToList();
        int droppedZones = zoneDocument.Rows.Count - knownZoneRows.Count;
        var zones = ZoneTableMapper.MapZones(
            zoneDocument.WithRows(knownZoneRows), zoneColumns, mapIds,
            resolveName: id => zoneStrings[id]);

        var creatureQuestStarterDocument = Load("tables/creature_quest_starter.tbl");
        TableShape.Expect("tables/creature_quest_starter.tbl", creatureQuestStarterDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"));
        var giversByQuest = new Dictionary<uint, uint>();
        foreach (var row in creatureQuestStarterDocument.Rows)
        {
            if (row[0] is uint creatureId && row[1] is uint questId) giversByQuest.TryAdd(questId, creatureId);
        }

        var creatureQuestFinisherDocument = Load("tables/creature_quest_finisher.tbl");
        TableShape.Expect("tables/creature_quest_finisher.tbl", creatureQuestFinisherDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"));
        var creatureQuestFinishers = creatureQuestFinisherDocument.Rows
            .Where(row => row[0] is uint entry && creatureEntries.Contains(entry) && row[1] is uint)
            .Select(row => new CreatureQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();

        var itemStarterQuestIds = items.Where(i => i.QuestId is not null)
            .Select(i => i.QuestId!.Value).ToHashSet();

        var staticObjectProtoDocument = Load("tables/staticobject_proto.tbl");
        var staticObjectProtoColumns = StaticObjectProtoColumns.Resolve(staticObjectProtoDocument);
        var staticObjectEntries = staticObjectProtoDocument.Rows
            .Where(row => row[0] is uint)
            .Select(row => (uint)row[0]).ToHashSet();
        var staticObjectQuestStarterDocument = Load("tables/staticobject_quest_starter.tbl");
        TableShape.Expect("tables/staticobject_quest_starter.tbl", staticObjectQuestStarterDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"));
        var staticObjectQuestStarters = staticObjectQuestStarterDocument.Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry) && row[1] is uint)
            .Select(row => new StaticObjectQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();
        var staticObjectQuestFinisherDocument = Load("tables/staticobject_quest_finisher.tbl");
        TableShape.Expect("tables/staticobject_quest_finisher.tbl", staticObjectQuestFinisherDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"));
        var staticObjectQuestFinishers = staticObjectQuestFinisherDocument.Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry) && row[1] is uint)
            .Select(row => new StaticObjectQuestBindingDefinition((uint)row[0], (uint)row[1]))
            .ToList();
        var staticObjectStarterQuestIds = staticObjectQuestStarters.Select(b => b.QuestId).ToHashSet();
        var staticObjectFinisherQuestIds = staticObjectQuestFinishers.Select(b => b.QuestId).ToHashSet();

        var staticObjectQuestItemBindingDocument = Load("tables/staticobject_quest_item_binding.tbl");
        TableShape.Expect("tables/staticobject_quest_item_binding.tbl", staticObjectQuestItemBindingDocument.Header.ColumnTypes,
            (0, "uk"), (1, "u"), (2, "u"), (3, "u"));
        var staticObjectQuestItemBindings = staticObjectQuestItemBindingDocument.Rows
            .Where(row => row[0] is uint entry && staticObjectEntries.Contains(entry)
                && row[1] is uint && row[2] is uint && row[3] is uint)
            .Select(row => new StaticObjectItemBindingDefinition((uint)row[0], (uint)row[1], (uint)row[2], (uint)row[3]))
            .ToList();

        var questDocument = Load("tables/quests.tbl");
        var questColumns = QuestColumns.Resolve(questDocument);
        var questsWithKnownGivers = questDocument.Rows.Where(row =>
            row[0] is uint id
            && ((giversByQuest.TryGetValue(id, out var giver) && creatureEntries.Contains(giver))
                || itemStarterQuestIds.Contains(id)
                || staticObjectStarterQuestIds.Contains(id)
                || staticObjectFinisherQuestIds.Contains(id))).ToList();
        int droppedQuests = questDocument.Rows.Count - questsWithKnownGivers.Count;

        var questGivers = giversByQuest.Values.Where(creatureEntries.Contains).ToHashSet();

        var greetingByEntry = new Dictionary<uint, uint>();
        if (creatureProtoColumns.GreetingMenu >= 0)
        {
            int greetingMenuColumn = creatureProtoColumns.GreetingMenu;
            foreach (var row in creatureProtoDocument.Rows)
            {
                if (row.Count > greetingMenuColumn && row[0] is uint entry && row[greetingMenuColumn] is uint menu && menu != 0)
                    greetingByEntry.TryAdd(entry, menu);
            }
        }
        else
        {
            Console.WriteLine($"  note: {build} has no readable greeting-menu column; gossip greetings skipped.");
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
        var quests = QuestTableMapper.MapQuests(
            questDocument.WithRows(questsWithKnownGivers), questColumns,
            npcEntries: npcs.Select(n => n.EntryId).ToHashSet(),
            resolveGiverEntry: id => giversByQuest.TryGetValue(id, out var giver) ? giver : null,
            nonCreatureStarterQuestIds: nonCreatureStarterQuestIds);

        var questIds = new HashSet<uint>(quests.Select(q => q.Id));
        var questRewards = QuestRewardTableMapper.MapRewards(
            questDocument.WithRows(questDocument.Rows.Where(row => row[0] is uint id && questIds.Contains(id))),
            QuestRewardColumns.Resolve(questDocument), questIds,
            resolveItem: entry => itemMetaByEntry.TryGetValue(entry, out var meta) ? meta : null);

        var teleportDocument = Load("tables/teleport_spot.tbl");
        var knownTeleportRows = teleportDocument.Rows.Where(row =>
            row[2] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedTeleportStops = teleportDocument.Rows.Count - knownTeleportRows.Count;
        var teleportStops = TeleportStopTableMapper.MapTeleportStops(
            teleportDocument.WithRows(knownTeleportRows), TeleportStopColumns.Resolve(teleportDocument), mapIds);

        var stopIds = new HashSet<uint>(teleportStops.Select(s => s.Id));
        var travelRouteDocument = Load("tables/t_bus_route.tbl");
        var knownTravelRouteRows = travelRouteDocument.Rows.Where(row =>
            row[1] is uint from && stopIds.Contains(from)
            && row[2] is uint to && stopIds.Contains(to) && from != to).ToList();
        int droppedTravelRoutes = travelRouteDocument.Rows.Count - knownTravelRouteRows.Count;
        var travelRoutes = TravelRouteTableMapper.MapTravelRoutes(
            travelRouteDocument.WithRows(knownTravelRouteRows), TravelRouteColumns.Resolve(travelRouteDocument), stopIds);

        var staticObjectDocument = Load("tables/staticobject_spawns.tbl");
        var knownStaticObjectRows = staticObjectDocument.Rows.Where(row =>
            row[2] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedStaticObjects = staticObjectDocument.Rows.Count - knownStaticObjectRows.Count;
        var staticObjects = StaticObjectTableMapper.MapStaticObjects(
            staticObjectDocument.WithRows(knownStaticObjectRows), StaticObjectColumns.Resolve(staticObjectDocument), mapIds);

        var graveyardDocument = Load("tables/map_graveyard.tbl");
        var knownGraveyardRows = graveyardDocument.Rows.Where(row =>
            row[6] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map)).ToList();
        int droppedGraveyards = graveyardDocument.Rows.Count - knownGraveyardRows.Count;
        var graveyards = MapGraveyardTableMapper.MapGraveyards(
            graveyardDocument.WithRows(knownGraveyardRows), MapGraveyardColumns.Resolve(graveyardDocument), mapIds);

        var gossip = GossipMenuTableMapper.MapMenus(
            Load("tables/chat_menu.tbl"),
            StringTableMapper.MapStrings(Load("tables/String_Menu_en.tbl")),
            zones.ToDictionary(zone => zone.Id, zone => zone.MapId));
        var gossipMenus = gossip.Menus;

        var storageUpgradeDocument = Load("tables/storage_upgrade.tbl");
        var storageUpgrades = StorageUpgradeTableMapper.MapUpgrades(
            storageUpgradeDocument, StorageUpgradeColumns.Resolve(storageUpgradeDocument));

        var staticObjectTypes = StaticObjectProtoTableMapper.MapTypes(
            staticObjectProtoDocument, staticObjectProtoColumns);

        var spellTalentDocument = Load("tables/spell_talent.tbl");
        var characterClassDocument = Load("tables/character_class.tbl");
        var (talentColumns, classColumns) = TalentColumns.Resolve(spellTalentDocument, characterClassDocument);
        var talentTrees = TalentTableMapper.MapTrees(
            spellTalentDocument, characterClassDocument, talentColumns, classColumns);

        Directory.CreateDirectory(outDirectory);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(Path.Combine(outDirectory, "maps.json"), JsonSerializer.Serialize(maps, options));
        File.WriteAllText(Path.Combine(outDirectory, "creatures.json"), JsonSerializer.Serialize(creatures, options));
        File.WriteAllText(Path.Combine(outDirectory, "creatureSpawns.json"), JsonSerializer.Serialize(spawns, options));
        File.WriteAllText(Path.Combine(outDirectory, "spells.json"), JsonSerializer.Serialize(spells, options));
        File.WriteAllText(Path.Combine(outDirectory, "items.json"), JsonSerializer.Serialize(items, options));
        File.WriteAllText(Path.Combine(outDirectory, "quests.json"), JsonSerializer.Serialize(quests, options));
        File.WriteAllText(Path.Combine(outDirectory, "questRewards.json"), JsonSerializer.Serialize(questRewards, options));
        File.WriteAllText(Path.Combine(outDirectory, "npcs.json"), JsonSerializer.Serialize(npcs, options));
        File.WriteAllText(Path.Combine(outDirectory, "vendorStock.json"), JsonSerializer.Serialize(vendorStock, options));
        File.WriteAllText(Path.Combine(outDirectory, "staticObjectQuestStarters.json"), JsonSerializer.Serialize(staticObjectQuestStarters, options));
        File.WriteAllText(Path.Combine(outDirectory, "staticObjectQuestFinishers.json"), JsonSerializer.Serialize(staticObjectQuestFinishers, options));
        File.WriteAllText(Path.Combine(outDirectory, "staticObjectQuestItemBindings.json"), JsonSerializer.Serialize(staticObjectQuestItemBindings, options));
        File.WriteAllText(Path.Combine(outDirectory, "creatureQuestFinishers.json"), JsonSerializer.Serialize(creatureQuestFinishers, options));
        File.WriteAllText(Path.Combine(outDirectory, "creatureLootTables.json"), JsonSerializer.Serialize(creatureLootTables, options));
        File.WriteAllText(Path.Combine(outDirectory, "teleportStops.json"), JsonSerializer.Serialize(teleportStops, options));
        File.WriteAllText(Path.Combine(outDirectory, "gossipMenus.json"), JsonSerializer.Serialize(gossipMenus, options));
        File.WriteAllText(Path.Combine(outDirectory, "mapGraveyards.json"), JsonSerializer.Serialize(graveyards, options));
        File.WriteAllText(Path.Combine(outDirectory, "travelRoutes.json"), JsonSerializer.Serialize(travelRoutes, options));
        File.WriteAllText(Path.Combine(outDirectory, "staticObjectSpawns.json"), JsonSerializer.Serialize(staticObjects, options));
        File.WriteAllText(Path.Combine(outDirectory, "storageUpgrades.json"), JsonSerializer.Serialize(storageUpgrades, options));
        File.WriteAllText(Path.Combine(outDirectory, "staticObjectTypes.json"), JsonSerializer.Serialize(staticObjectTypes, options));
        File.WriteAllText(Path.Combine(outDirectory, "talentTrees.json"), JsonSerializer.Serialize(talentTrees, options));
        // Only newer builds ship the table; older packages keep no trace of it.
        var dungeonRewardsFile = dungeonRewardDocument is not null ? "dungeonRewards.json" : null;
        if (dungeonRewardsFile is not null)
        {
            File.WriteAllText(Path.Combine(outDirectory, dungeonRewardsFile), JsonSerializer.Serialize(dungeonRewards, options));
        }
        WriteTerrain(archiveByMapId, outDirectory);

        var datasets = new Dictionary<string, string>
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
        };
        if (dungeonRewardsFile is not null)
        {
            datasets["dungeonRewards"] = dungeonRewardsFile;
        }

        var manifest = new DataManifest(
            ExtractorVersion: "0.1.0",
            GameVersion: build,
            SourceHash: sourceHash,
            GeneratedAt: DateTimeOffset.UtcNow,
            Datasets: datasets);
        DataManifestJson.Save(manifest, Path.Combine(outDirectory, DataManifest.FileName));

        return new ImportResult(
            maps.Count, creatures.Count, spawns.Count, droppedSpawns, spells.Count, items.Count,
            zones.Count, droppedZones, quests.Count, droppedQuests, npcs.Count, questRewards.Count,
            teleportStops.Count, droppedTeleportStops, travelRoutes.Count, droppedTravelRoutes,
            staticObjects.Count, droppedStaticObjects, storageUpgrades.Count, staticObjectTypes.Count,
            vendorStock.Count, staticObjectQuestStarters.Count, staticObjectQuestFinishers.Count,
            staticObjectQuestItemBindings.Count, creatureQuestFinishers.Count,
            creatureLootTables.Count,
            graveyards.Count, droppedGraveyards,
            gossipMenus.Count, gossip.DroppedTeleports,
            talentTrees.Count,
            dungeonRewards.Count,
            build,
            string.Join(",", effectSlotOrder));
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
