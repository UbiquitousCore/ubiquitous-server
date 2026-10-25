using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

// The resolvers behind the import: exact 1.1.4 shapes take the pinned
// columns unchanged; anything else is located by shape or fails loudly.
// These tests pin both halves, plus that the pinned shapes stayed
// identical to the checked-in golden fingerprint.
public class TableResolveTests
{
    private static string FindGolden(string build)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "asset-extractor", "fingerprints", $"{build}.json");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "fingerprints", $"{build}.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate fingerprints/{build}.json above the test run.");
    }

    private static Dictionary<string, string?> LoadGolden(string build)
    {
        using var stream = File.OpenRead(FindGolden(build));
        using var json = System.Text.Json.JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("tables").EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("name").GetString()!,
                p => p.GetProperty("format").GetString());
    }

    [Fact]
    public void PinnedSchemas_MatchTheGoldenFingerprint()
    {
        foreach (var build in TableSchemas.Known)
        {
            var golden = LoadGolden(build.Build);
            foreach (var table in TableSchemas.LoadBearingTables)
            {
                // A build without a table (2.2.0 has no loot tables) pins
                // no shape for it; anything else must match exactly.
                if (!golden.TryGetValue(table, out var format))
                {
                    Assert.Null(TableSchemas.FormatFor(table, build.Build));
                    continue;
                }

                Assert.Equal(format, TableSchemas.FormatFor(table, build.Build));
            }
        }
    }

    [Fact]
    public void Resolvers_ReturnPinnedColumns_ForGoldenShapes()
    {
        foreach (var table in TableSchemas.LoadBearingTables)
        {
            // A table a build never shipped (dungeon rewards predate
            // nothing here; loot postdates 1.1.4) has no resolver case.
            var format = TableSchemas.FormatFor(table, "1.1.4")
                ?? TableSchemas.FormatFor(table, "2.2.0");
            if (format is null) continue;
            var document = Doc(table, format, rows: 0);
            object resolved = table switch
            {
                "tables/map_info.tbl" => MapInfoColumns.Resolve(document),
                "tables/zones.tbl" => ZoneColumns.Resolve(document),
                "tables/staticobject_spawns.tbl" => StaticObjectColumns.Resolve(document),
                "tables/staticobject_proto.tbl" => StaticObjectProtoColumns.Resolve(document),
                "tables/creature_spawns.tbl" => CreatureSpawnColumns.Resolve(document),
                "tables/creature_proto.tbl" => CreatureProtoColumns.Resolve(document),
                "tables/creature_appearance.tbl" => CreatureAppearanceTableMapper.Resolve(document),
                "tables/spell_effect.tbl" => SpellEffectColumns.Resolve(document),
                "tables/spell_new.tbl" => SpellNewColumns.Resolve(document, new HashSet<uint>()),
                "tables/items.tbl" => ItemColumns.Resolve(document),
                "tables/quests.tbl" => QuestColumns.Resolve(document),
                "tables/chat_menu.tbl" => ResolveChatMenu(document),
                "tables/teleport_spot.tbl" => TeleportStopColumns.Resolve(document),
                "tables/t_bus_route.tbl" => TravelRouteColumns.Resolve(document),
                "tables/map_graveyard.tbl" => MapGraveyardColumns.Resolve(document),
                "tables/storage_upgrade.tbl" => StorageUpgradeColumns.Resolve(document),
                "tables/spell_talent.tbl" => TalentColumns.Resolve(document, Doc("tables/character_class.tbl",
                    TableSchemas.FormatFor("tables/character_class.tbl", "1.1.4")!, rows: 0)),
                "tables/character_class.tbl" => TalentColumns.Resolve(
                    Doc("tables/spell_talent.tbl", TableSchemas.FormatFor("tables/spell_talent.tbl", "1.1.4")!, rows: 0),
                    document),
                // Read by the client, never by this import: the fingerprint
                // and build identity already cover their drift.
                "tables/staticobject_displayinfo.tbl" or "tables/creaturedisplayinfo.tbl"
                    or "tables/creaturemodeldata.tbl" or "tables/itemdisplayinfo.tbl"
                    => "client-only",
                "tables/creature_quest_starter.tbl" or "tables/creature_quest_finisher.tbl"
                    or "tables/staticobject_quest_starter.tbl" or "tables/staticobject_quest_finisher.tbl"
                    => ExpectColumns(table, document, (0, "uk"), (1, "u")),
                "tables/staticobject_quest_item_binding.tbl"
                    => ExpectColumns(table, document, (0, "uk"), (1, "u"), (2, "u"), (3, "u")),
                "tables/creature_machant_sell_program.tbl"
                    => ExpectColumns(table, document, (0, "uk"), (1, "s"), (2, "u")),
                "tables/creature_machant_sell_program_item.tbl"
                    => ExpectColumns(table, document, (0, "uk"), (1, "u"), (2, "u"), (3, "u"), (4, "u")),
                "tables/dungeon_reward.tbl" => DungeonRewardColumns.Resolve(document),
                "tables/loot.tbl"
                    => ExpectColumns(table, document, (1, "u"), (2, "u"), (3, "u")),
                "tables/loot_feature.tbl"
                    => ExpectColumns(table, document, (0, "uk"), (2, "u"), (3, "u"), (6, "u"), (7, "u")),
                "tables/loot_feature_items.tbl"
                    => ExpectColumns(table, document, (1, "u"), (2, "u"), (3, "f"), (4, "u"), (5, "u")),
                _ => throw new InvalidOperationException($"no resolver case for {table}"),
            };
            Assert.NotNull(resolved);
        }
    }

    [Fact]
    public void MapInfo_FindsTheNamePair_WhenColumnsMove()
    {
        // An extra leading column pushes the adjacent string pair along.
        var document = Doc("tables/map_info.tbl", "uuuuuufffussuuufuuu", rows: 0);
        var columns = MapInfoColumns.Resolve(document);
        Assert.Equal(10, columns.InternalName);
        Assert.Equal(11, columns.File);
    }

    [Fact]
    public void MapInfo_ThrowsLoudly_WithNoNamePair()
    {
        var document = Doc("tables/map_info.tbl", "uuuuufffusuufuuu", rows: 0);
        var ex = Assert.Throws<FTableFormatException>(() => MapInfoColumns.Resolve(document));
        Assert.Contains("tables/map_info.tbl", ex.Message);
        Assert.Contains("adjacent pair", ex.Message);
    }

    [Fact]
    public void Zone_ThrowsLoudly_WhenABoundChangesType()
    {
        var broken = TableSchemas.FormatFor("tables/zones.tbl", "1.1.4")!.ToCharArray();
        broken[4] = 'u';
        var document = Doc("tables/zones.tbl", new string(broken), rows: 0);
        var ex = Assert.Throws<FTableFormatException>(() => ZoneColumns.Resolve(document));
        Assert.Contains("tables/zones.tbl", ex.Message);
        Assert.Contains("column 4", ex.Message);
    }

    [Fact]
    public void CreatureProto_DegradesToSkippedWiring_WhenListColumnsAreRetyped()
    {
        var changed = TableSchemas.FormatFor("tables/creature_proto.tbl", "1.1.4")!.ToCharArray();
        changed[55] = 'u';
        changed[56] = 's';
        var document = Doc("tables/creature_proto.tbl", new string(changed), rows: 0);
        var columns = CreatureProtoColumns.Resolve(document);
        Assert.Equal(-1, columns.SellList);
        Assert.Equal(-1, columns.GreetingMenu);
        Assert.Equal(0, columns.Entry);
    }

    [Fact]
    public void SpellNew_AcceptsTrailingColumns_WithoutChangingPins()
    {
        var document = Doc("tables/spell_new.tbl",
            TableSchemas.FormatFor("tables/spell_new.tbl", "1.1.4")! + "uu", rows: 0);
        var (columns, notes) = SpellNewColumns.Resolve(document, new HashSet<uint> { 500u });
        Assert.Same(SpellNewColumns.Build114, columns);
        Assert.Single(notes);
    }

    [Fact]
    public void Talent220_ReadsStrideFiveRankSpells()
    {
        var talents = DocWith("tables/spell_talent.tbl",
            TableSchemas.FormatFor("tables/spell_talent.tbl", "2.2.0")!,
        [
            new Dictionary<int, object>
            {
                [0] = 1u, [1] = 1u, [2] = 1u, [3] = 11u, [8] = 3u,
                [9] = 61u, [14] = 62u, [19] = 63u,
            },
            new Dictionary<int, object>
            {
                [0] = 2u, [1] = 2u, [2] = 1u, [3] = 11u, [4] = 5u, [5] = 1u, [8] = 1u,
                [9] = 69u,
            },
        ]);
        var classes = DocWith("tables/character_class.tbl",
            TableSchemas.FormatFor("tables/character_class.tbl", "2.2.0")!,
            [new Dictionary<int, object> { [0] = 1u, [3] = 11u }]);
        var (talentColumns, classColumns) = TalentColumns.Resolve(talents, classes);
        var trees = TalentTableMapper.MapTrees(talents, classes, talentColumns, classColumns);
        var tree = Assert.Single(trees);
        Assert.Equal([61u, 62u, 63u], tree.Talents.Single(t => t.Id == 1u).RankSpellIds);
        Assert.Equal([1u], tree.Talents.Single(t => t.Id == 2u).Prerequisites);
    }

    [Fact]
    public void SpellNew_ThrowsLoudly_WhenTheCoreMoves()
    {
        var shifted = "u" + TableSchemas.FormatFor("tables/spell_new.tbl", "1.1.4")!;
        var document = Doc("tables/spell_new.tbl", shifted, rows: 0);
        var ex = Assert.Throws<FTableFormatException>(
            () => SpellNewColumns.Resolve(document, new HashSet<uint> { 500u }));
        Assert.Contains("tables/spell_new.tbl", ex.Message);
    }

    private static object ResolveChatMenu(FTableDocument document)
    {
        GossipMenuTableMapper.MapMenus(document, new Dictionary<uint, string>(), new Dictionary<uint, ushort>());
        return "ok";
    }

    private static object ExpectColumns(string table, FTableDocument document, params (int Column, string Types)[] checks)
    {
        TableShape.Expect(table, document.Header.ColumnTypes, checks);
        return "ok";
    }

    private static FTableDocument Doc(string name, string format, int rows) =>
        DocWith(name, format, Enumerable.Range(0, rows).Select(_ => new Dictionary<int, object>()).ToList());

    private static FTableDocument DocWith(string name, string format, IReadOnlyList<Dictionary<int, object>> rows)
    {
        var widths = format.Select(c => c switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 }).ToArray();
        int stride = widths.Sum();
        const int header = 44;
        int formatAt = header + rows.Count * 4;
        int recordsAt = formatAt + format.Length;
        var data = new byte[recordsAt + stride * rows.Count];
        void Ascii(int at, string text)
        {
            for (int i = 0; i < text.Length; i++) data[at + i] = (byte)text[i];
        }

        void U32(int at, int value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)value);
        Ascii(0, "FTABLE");
        U32(12, rows.Count); U32(16, format.Length); U32(20, header); U32(24, formatAt);
        U32(28, recordsAt); U32(32, stride); U32(36, recordsAt + stride * rows.Count); U32(40, 0);
        Ascii(formatAt, format);
        int at = recordsAt;
        foreach (var row in rows)
        {
            for (int c = 0; c < format.Length; c++)
            {
                if (format[c] == 'f')
                {
                    float value = row.TryGetValue(c, out var v) ? Convert.ToSingle(v) : 0f;
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), value);
                }
                else
                {
                    uint value = row.TryGetValue(c, out var v) ? Convert.ToUInt32(v) : 0u;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
                }

                at += widths[c];
            }
        }

        return FTableDocument.Load(data);
    }
}
