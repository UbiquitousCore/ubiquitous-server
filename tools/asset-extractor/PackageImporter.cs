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
        IReadOnlyList<string> Notes);

    public static ImportResult Import(
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

        var notes = new List<string>();

        // A table some builds do not ship: what depends on it is left out, and said.
        FTableDocument? LoadOptional(string name, string without)
        {
            if (archiveEntries.TryGetValue(name, out var bytes)) return FTableDocument.Load(bytes);
            notes.Add($"{name}: not in this build; {without}.");
            return null;
        }

        ResolvedColumns Columns(ColumnLayout layout, FTableDocument document)
        {
            var resolved = layout.Resolve(document);
            notes.AddRange(resolved.Notes);
            return resolved;
        }

        string build = KnownBuilds.Identify(TableFingerprint.Capture(archiveEntries, sourceHash));
        var mapDocument = Load("tables/map_info.tbl");
        var maps = MapTableMapper.MapMaps(mapDocument, Columns(MapInfoColumns.Layout, mapDocument).As<MapTableColumns>());
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
            creatureAppearanceDocument,
            Columns(CreatureAppearanceColumns.Layout, creatureAppearanceDocument).As<CreatureAppearanceTableColumns>());
        var creatureStrings = StringTableMapper.MapStrings(Load("tables/String_Creature_en.tbl"));
        var creatureProtoDocument = Load("tables/creature_proto.tbl");
        var creatureProtoColumns = Columns(CreatureProtoColumns.Layout, creatureProtoDocument).As<CreatureTableColumns>();
        var creatures = CreatureTableMapper.MapCreatures(creatureProtoDocument, creatureProtoColumns,
            resolveName: entry => creatureNameStringIds.TryGetValue(entry, out var stringId)
                && creatureStrings.TryGetValue(stringId, out var text)
                && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"creature-{entry}");

        var mapIds = new HashSet<ushort>(maps.Select(m => m.Id));
        var creatureEntries = new HashSet<uint>(creatures.Select(c => c.EntryId));
        bool OnKnownMap(IReadOnlyList<object> row, int mapColumn) =>
            row[mapColumn] is uint map && map <= ushort.MaxValue && mapIds.Contains((ushort)map);

        var spawnDocument = Load("tables/creature_spawns.tbl");
        var spawnColumns = Columns(CreatureSpawnColumns.Layout, spawnDocument).As<CreatureSpawnTableColumns>();
        var knownSpawnRows = spawnDocument.Rows.Where(row =>
            row[spawnColumns.CreatureEntryId] is uint entry && creatureEntries.Contains(entry)
            && OnKnownMap(row, spawnColumns.MapId)).ToList();
        int droppedSpawns = spawnDocument.Rows.Count - knownSpawnRows.Count;
        var spawns = CreatureSpawnTableMapper.MapSpawns(
            spawnDocument.WithRows(knownSpawnRows), spawnColumns, mapIds, creatureEntries);

        var spellEffectDocument = Load("tables/spell_effect.tbl");
        var spellEffectColumns = Columns(SpellEffectColumns.Layout, spellEffectDocument).As<SpellEffectTableColumns>();
        var spellEffectsById = SpellEffectTableMapper.MapEffects(spellEffectDocument, spellEffectColumns)
            .ToDictionary(e => e.Id);
        var spellStrings = StringTableMapper.MapStrings(Load("tables/String_Spell_en.tbl"));
        var spellNewDocument = Load("tables/spell_new.tbl");
        var spellNewColumns = Columns(SpellNewColumns.Layout, spellNewDocument).As<SpellTableColumns>();
        var spells = SpellTableMapper.MapSpells(spellNewDocument, spellNewColumns,
            resolveName: id => spellStrings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"spell-{id}");

        var spellRowsById = spellNewDocument.Rows.ToDictionary(row => (uint)row[spellNewColumns.Id]);
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
        var itemColumns = Columns(ItemColumns.Layout, itemDocument).As<ItemTableColumns>();
        var itemRows = itemDocument.Rows.Where(row =>
            ItemTableMapper.FitsKind(row[itemColumns.Class]) && ItemTableMapper.FitsKind(row[itemColumns.SubClass])).ToList();
        if (itemRows.Count < itemDocument.Rows.Count)
        {
            notes.Add($"tables/items.tbl: {itemDocument.Rows.Count - itemRows.Count} item(s) dropped: " +
                "a class or subclass larger than the server keeps.");
        }

        itemDocument = itemDocument.WithRows(itemRows);
        var items = ItemTableMapper.MapItems(itemDocument, itemColumns,
            resolveName: id => itemStrings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"item-{id}");

        var itemMetaByEntry = new Dictionary<uint, (uint NameTextId, int Quality)>();
        foreach (var row in itemDocument.Rows)
        {
            if (row[itemColumns.Id] is uint entry && row[itemColumns.NameTextId] is uint nameId)
            {
                int quality = itemColumns.Quality >= 0 && row[itemColumns.Quality] is uint known ? unchecked((int)known) : 0;
                itemMetaByEntry.TryAdd(entry, (nameId, quality));
            }
        }

        var sellListsByCreature = new Dictionary<uint, IReadOnlyList<uint>>();
        if (creatureProtoColumns.SellList >= 0)
        {
            int sellListColumn = creatureProtoColumns.SellList;
            foreach (var row in creatureProtoDocument.Rows)
            {
                if (row[creatureProtoColumns.Entry] is not uint creatureEntry) continue;
                IReadOnlyList<uint> lists = row[sellListColumn] switch
                {
                    string several => SellListIds.Parse(several),
                    uint one when one != 0 => [one],
                    _ => [],
                };
                if (lists.Count > 0) sellListsByCreature[creatureEntry] = lists;
            }
        }

        var sellProgramCurrency = new Dictionary<uint, uint>();
        if (LoadOptional("tables/creature_machant_sell_program.tbl", "vendors sell for coin") is { } sellProgramDocument)
        {
            var listColumns = Columns(VendorColumns.ListLayout, sellProgramDocument).As<VendorListTableColumns>();
            foreach (var row in sellProgramDocument.Rows)
            {
                if (row[listColumns.Id] is uint vendorListId && row[listColumns.Currency] is uint currencyId)
                {
                    sellProgramCurrency.TryAdd(vendorListId, currencyId);
                }
            }
        }

        var sellProgramItems = new Dictionary<uint, List<(uint ItemId, int StackCount, int MaxCount)>>();
        var sellProgramItemDocument = LoadOptional("tables/creature_machant_sell_program_item.tbl", "vendors stock nothing");
        var listItemColumns = sellProgramItemDocument is null
            ? null
            : Columns(VendorColumns.ItemLayout, sellProgramItemDocument).As<VendorListItemTableColumns>();
        foreach (var row in sellProgramItemDocument?.Rows ?? [])
        {
            if (row[listItemColumns!.List] is uint vendorListId && row[listItemColumns.Item] is uint itemId
                && row[listItemColumns.StackCount] is uint stackCount && row[listItemColumns.MaxCount] is uint maxCount)
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
        var lootItemsByGroup = new Dictionary<uint, List<LootTableItemDefinition>>();
        var lootGroupsById = new Dictionary<uint, (int MinLevel, int MaxLevel, int GuaranteedDrops, int MaxDrops)>();
        var lootGroupsByCreature = new Dictionary<uint, List<LootGroupDefinition>>();
        const string WithoutLoot = "creatures drop nothing";
        if (LoadOptional("tables/loot_feature_items.tbl", WithoutLoot) is { } lootItemsDocument
            && LoadOptional("tables/loot_feature.tbl", WithoutLoot) is { } lootFeatureDocument
            && LoadOptional("tables/loot.tbl", WithoutLoot) is { } lootDocument)
        {
            var lootItemColumns = Columns(LootColumns.ItemLayout, lootItemsDocument).As<LootItemTableColumns>();
            foreach (var row in lootItemsDocument.Rows)
            {
                if (row[lootItemColumns.Group] is not uint groupId || row[lootItemColumns.Item] is not uint itemId
                    || row[lootItemColumns.Chance] is not float chance
                    || row[lootItemColumns.Fewest] is not uint fewest || row[lootItemColumns.Most] is not uint most)
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

            var lootGroupColumns = Columns(LootColumns.GroupLayout, lootFeatureDocument).As<LootGroupTableColumns>();
            foreach (var row in lootFeatureDocument.Rows)
            {
                if (row[lootGroupColumns.Id] is not uint groupId
                    || row[lootGroupColumns.MinLevel] is not uint lowestLevel
                    || row[lootGroupColumns.MaxLevel] is not uint highestLevel
                    || row[lootGroupColumns.GuaranteedDrops] is not uint guaranteed
                    || row[lootGroupColumns.MaxDrops] is not uint upTo)
                {
                    continue;
                }

                lootGroupsById[groupId] = (
                    unchecked((int)lowestLevel), unchecked((int)highestLevel),
                    unchecked((int)guaranteed), unchecked((int)upTo));
            }

            const uint CreatureLootTargetType = 1;
            var lootColumns = Columns(LootColumns.Layout, lootDocument).As<LootTableColumns>();
            foreach (var row in lootDocument.Rows)
            {
                if (row[lootColumns.Target] is not uint targetId
                    || row[lootColumns.TargetKind] is not uint targetKind || targetKind != CreatureLootTargetType
                    || row[lootColumns.Group] is not uint groupId)
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

        // Only later builds carry dungeon rewards; an older one is not missing them.
        var dungeonRewardDocument = archiveEntries.TryGetValue("tables/dungeon_reward.tbl", out var dungeonRewardBytes)
            ? FTableDocument.Load(dungeonRewardBytes)
            : null;
        var dungeonRewards = new List<DungeonRewardDefinition>();
        if (dungeonRewardDocument is not null)
        {
            dungeonRewards.AddRange(DungeonRewardTableMapper.MapRewards(
                dungeonRewardDocument,
                Columns(DungeonRewardColumns.Layout, dungeonRewardDocument).As<DungeonRewardTableColumns>(),
                mapIds, new HashSet<uint>(itemsByEntry.Keys)));
        }

        var zoneStrings = StringTableMapper.MapStrings(Load("tables/String_Zones_en.tbl"));
        var zoneDocument = Load("tables/zones.tbl");
        var zoneColumns = Columns(ZoneColumns.Layout, zoneDocument).As<ZoneTableColumns>();
        var knownZoneRows = zoneDocument.Rows.Where(row =>
            OnKnownMap(row, zoneColumns.MapId)
            && row[zoneColumns.Id] is int id && zoneStrings.ContainsKey(unchecked((uint)id))
            && row[zoneColumns.MinX] is float minX && row[zoneColumns.MaxX] is float maxX && minX <= maxX
            && row[zoneColumns.MinZ] is float minZ && row[zoneColumns.MaxZ] is float maxZ && minZ <= maxZ).ToList();
        int droppedZones = zoneDocument.Rows.Count - knownZoneRows.Count;
        var zones = ZoneTableMapper.MapZones(
            zoneDocument.WithRows(knownZoneRows), zoneColumns, mapIds,
            resolveName: id => zoneStrings[id]);

        // (entry, quest) pairs of one binding table; none when the build lacks it.
        List<(uint Entry, uint Quest)> Bindings(ColumnLayout layout, string without)
        {
            if (LoadOptional(layout.Table, without) is not { } document) return [];
            var columns = Columns(layout, document).As<QuestBindingTableColumns>();
            return document.Rows
                .Where(row => row[columns.Entry] is uint && row[columns.Quest] is uint)
                .Select(row => ((uint)row[columns.Entry], (uint)row[columns.Quest]))
                .ToList();
        }

        var giversByQuest = new Dictionary<uint, uint>();
        foreach (var (creatureId, questId) in Bindings(QuestBindingColumns.CreatureStarter, "no creature gives a quest"))
        {
            giversByQuest.TryAdd(questId, creatureId);
        }

        var creatureQuestFinishers = Bindings(QuestBindingColumns.CreatureFinisher, "no creature takes a quest in")
            .Where(binding => creatureEntries.Contains(binding.Entry))
            .Select(binding => new CreatureQuestBindingDefinition(binding.Entry, binding.Quest))
            .ToList();

        var itemStarterQuestIds = items.Where(i => i.QuestId is not null)
            .Select(i => i.QuestId!.Value).ToHashSet();

        var staticObjectProtoDocument = Load("tables/staticobject_proto.tbl");
        var staticObjectProtoColumns =
            Columns(StaticObjectProtoColumns.Layout, staticObjectProtoDocument).As<StaticObjectProtoTableColumns>();
        var staticObjectEntries = staticObjectProtoDocument.Rows
            .Where(row => row[staticObjectProtoColumns.Entry] is uint)
            .Select(row => (uint)row[staticObjectProtoColumns.Entry]).ToHashSet();
        var staticObjectQuestStarters = Bindings(QuestBindingColumns.StaticObjectStarter, "no object gives a quest")
            .Where(binding => staticObjectEntries.Contains(binding.Entry))
            .Select(binding => new StaticObjectQuestBindingDefinition(binding.Entry, binding.Quest))
            .ToList();
        var staticObjectQuestFinishers = Bindings(QuestBindingColumns.StaticObjectFinisher, "no object takes a quest in")
            .Where(binding => staticObjectEntries.Contains(binding.Entry))
            .Select(binding => new StaticObjectQuestBindingDefinition(binding.Entry, binding.Quest))
            .ToList();
        var staticObjectStarterQuestIds = staticObjectQuestStarters.Select(b => b.QuestId).ToHashSet();
        var staticObjectFinisherQuestIds = staticObjectQuestFinishers.Select(b => b.QuestId).ToHashSet();

        var staticObjectQuestItemBindings = new List<StaticObjectItemBindingDefinition>();
        if (LoadOptional(QuestBindingColumns.StaticObjectItem.Table, "no object hands over a quest item") is { } itemBindingDocument)
        {
            var binding = Columns(QuestBindingColumns.StaticObjectItem, itemBindingDocument).As<QuestItemBindingTableColumns>();
            staticObjectQuestItemBindings.AddRange(itemBindingDocument.Rows
                .Where(row => row[binding.Entry] is uint entry && staticObjectEntries.Contains(entry)
                    && row[binding.Quest] is uint && row[binding.Item] is uint && row[binding.Reserve] is uint)
                .Select(row => new StaticObjectItemBindingDefinition(
                    (uint)row[binding.Entry], (uint)row[binding.Quest], (uint)row[binding.Item], (uint)row[binding.Reserve])));
        }

        var questDocument = Load("tables/quests.tbl");
        var questLayout = Columns(QuestColumns.Layout, questDocument);
        var questColumns = questLayout.As<QuestTableColumns>();
        var questsWithKnownGivers = questDocument.Rows.Where(row =>
            row[questColumns.Id] is uint id
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
                if (row[creatureProtoColumns.Entry] is uint entry && row[greetingMenuColumn] is uint menu && menu != 0)
                    greetingByEntry.TryAdd(entry, menu);
            }
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
            questDocument.WithRows(questDocument.Rows.Where(row => row[questColumns.Id] is uint id && questIds.Contains(id))),
            questLayout.As<QuestRewardTableColumns>(), questIds,
            resolveItem: entry => itemMetaByEntry.TryGetValue(entry, out var meta) ? meta : null);

        IReadOnlyList<TeleportStopDefinition> teleportStops = [];
        int droppedTeleportStops = 0;
        if (LoadOptional("tables/teleport_spot.tbl", "there are no teleport stops") is { } teleportDocument)
        {
            var teleportColumns = Columns(TeleportStopColumns.Layout, teleportDocument).As<TeleportStopTableColumns>();
            var knownTeleportRows = teleportDocument.Rows.Where(row => OnKnownMap(row, teleportColumns.MapId)).ToList();
            droppedTeleportStops = teleportDocument.Rows.Count - knownTeleportRows.Count;
            teleportStops = TeleportStopTableMapper.MapTeleportStops(
                teleportDocument.WithRows(knownTeleportRows), teleportColumns, mapIds);
        }

        var stopIds = new HashSet<uint>(teleportStops.Select(s => s.Id));
        IReadOnlyList<TravelRouteDefinition> travelRoutes = [];
        int droppedTravelRoutes = 0;
        if (LoadOptional("tables/t_bus_route.tbl", "there are no travel routes") is { } travelRouteDocument)
        {
            var routeColumns = Columns(TravelRouteColumns.Layout, travelRouteDocument).As<TravelRouteTableColumns>();
            var knownTravelRouteRows = travelRouteDocument.Rows.Where(row =>
                row[routeColumns.FirstStop] is uint from && stopIds.Contains(from)
                && row[routeColumns.SecondStop] is uint to && stopIds.Contains(to) && from != to).ToList();
            droppedTravelRoutes = travelRouteDocument.Rows.Count - knownTravelRouteRows.Count;
            travelRoutes = TravelRouteTableMapper.MapTravelRoutes(
                travelRouteDocument.WithRows(knownTravelRouteRows), routeColumns, stopIds);
        }

        var staticObjectDocument = Load("tables/staticobject_spawns.tbl");
        var staticObjectColumns = Columns(StaticObjectColumns.Layout, staticObjectDocument).As<StaticObjectTableColumns>();
        var knownStaticObjectRows = staticObjectDocument.Rows
            .Where(row => OnKnownMap(row, staticObjectColumns.MapId)).ToList();
        int droppedStaticObjects = staticObjectDocument.Rows.Count - knownStaticObjectRows.Count;
        var staticObjects = StaticObjectTableMapper.MapStaticObjects(
            staticObjectDocument.WithRows(knownStaticObjectRows), staticObjectColumns, mapIds);

        IReadOnlyList<MapGraveyardDefinition> graveyards = [];
        int droppedGraveyards = 0;
        if (LoadOptional("tables/map_graveyard.tbl", "the dead rise where they fell") is { } graveyardDocument)
        {
            var graveyardColumns = Columns(MapGraveyardColumns.Layout, graveyardDocument).As<MapGraveyardTableColumns>();
            var knownGraveyardRows = graveyardDocument.Rows.Where(row => OnKnownMap(row, graveyardColumns.MapId)).ToList();
            droppedGraveyards = graveyardDocument.Rows.Count - knownGraveyardRows.Count;
            graveyards = MapGraveyardTableMapper.MapGraveyards(
                graveyardDocument.WithRows(knownGraveyardRows), graveyardColumns, mapIds);
        }

        IReadOnlyList<GossipMenuDefinition> gossipMenus = [];
        int droppedGossipTeleports = 0;
        if (LoadOptional("tables/chat_menu.tbl", "creatures have nothing to say") is { } menuDocument)
        {
            var gossip = GossipMenuTableMapper.MapMenus(
                menuDocument,
                Columns(GossipMenuColumns.Layout, menuDocument).As<GossipMenuTableColumns>(),
                StringTableMapper.MapStrings(Load("tables/String_Menu_en.tbl")),
                zones.ToDictionary(zone => zone.Id, zone => zone.MapId));
            gossipMenus = gossip.Menus;
            droppedGossipTeleports = gossip.DroppedTeleports;
        }

        IReadOnlyList<StorageUpgradeDefinition> storageUpgrades = [];
        if (LoadOptional("tables/storage_upgrade.tbl", "bags and the bank cannot be enlarged") is { } storageUpgradeDocument)
        {
            storageUpgrades = StorageUpgradeTableMapper.MapUpgrades(
                storageUpgradeDocument,
                Columns(StorageUpgradeColumns.Layout, storageUpgradeDocument).As<StorageUpgradeTableColumns>());
        }

        var staticObjectTypes = StaticObjectProtoTableMapper.MapTypes(
            staticObjectProtoDocument, staticObjectProtoColumns);

        IReadOnlyList<TalentTreeDefinition> talentTrees = [];
        if (LoadOptional("tables/spell_talent.tbl", "there are no talents") is { } spellTalentDocument
            && LoadOptional("tables/character_class.tbl", "there are no talents") is { } characterClassDocument)
        {
            talentTrees = TalentTableMapper.MapTrees(
                spellTalentDocument, characterClassDocument,
                Columns(TalentColumns.Layout, spellTalentDocument).As<TalentTableColumns>(),
                Columns(TalentColumns.ClassLayout, characterClassDocument).As<ClassTreeColumns>());
        }

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
            Datasets: datasets,
            ImportNotes: notes.Count > 0 ? notes : null);
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
            gossipMenus.Count, droppedGossipTeleports,
            talentTrees.Count,
            dungeonRewards.Count,
            build,
            notes);
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
