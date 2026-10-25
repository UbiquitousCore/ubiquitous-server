namespace Ubiquitous.AssetExtractor;

// Phase 1 foundation: which table shapes belong to which game build.
// Each entry is the exact format string fingerprinted from that build's
// tables.bin (see fingerprints/1.1.4.json). The importer uses this to name
// the build it is reading and to report drift on the load-bearing tables
// instead of failing later with a bare column error.
//
// Adding a build: fingerprint it (--fingerprint), copy the formats of the
// tables below into a new Build entry, and record the result in docs.
// Only tables the import actually reads are listed; string tables and
// decoration never block an import.
public static class TableSchemas
{
    public sealed record BuildSchemas(
        string Build,
        IReadOnlyDictionary<string, string> Formats,
        IReadOnlyList<string>? PresentTables = null);

    // Tables the package import reads. A shape change in any of these is a
    // real import risk, so drift here is reported loudly.
    // dungeon_reward only exists from 2.2.0 on; older captures simply
    // lack it, which IdentifyBuild skips over.
    public static readonly IReadOnlyList<string> LoadBearingTables = new[]
    {
        "tables/dungeon_reward.tbl",
        "tables/map_info.tbl",
        "tables/zones.tbl",
        "tables/staticobject_spawns.tbl",
        "tables/staticobject_proto.tbl",
        "tables/staticobject_displayinfo.tbl",
        "tables/creature_spawns.tbl",
        "tables/creature_appearance.tbl",
        "tables/creaturedisplayinfo.tbl",
        "tables/creaturemodeldata.tbl",
        "tables/creature_proto.tbl",
        "tables/spell_new.tbl",
        "tables/spell_effect.tbl",
        "tables/items.tbl",
        "tables/itemdisplayinfo.tbl",
        "tables/quests.tbl",
        "tables/chat_menu.tbl",
        "tables/teleport_spot.tbl",
        "tables/t_bus_route.tbl",
        "tables/map_graveyard.tbl",
        "tables/storage_upgrade.tbl",
        "tables/spell_talent.tbl",
        "tables/character_class.tbl",
        "tables/creature_quest_starter.tbl",
        "tables/creature_quest_finisher.tbl",
        "tables/creature_machant_sell_program.tbl",
        "tables/creature_machant_sell_program_item.tbl",
        "tables/loot.tbl",
        "tables/loot_feature.tbl",
        "tables/loot_feature_items.tbl",
    };

    public static readonly IReadOnlyList<BuildSchemas> Known = new[]
    {
        // 2.2.0 drops the loot tables entirely (loot moves to dungeon
        // rewards), so they are absent here: IdentifyBuild skips tables a
        // capture lacks, and the importer degrades to no loot with a note.
        new BuildSchemas("2.2.0", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tables/map_info.tbl"] = "uuuuuffffffussuuufuuu",
            ["tables/zones.tbl"] = "iiiuffffffffuuffffuuuussssssfffffusiiiu",
            ["tables/staticobject_spawns.tbl"] = "uuuffffffuufffuf",
            ["tables/staticobject_proto.tbl"] = "uuuuuuufuuuuuuuuuuuu",
            ["tables/staticobject_displayinfo.tbl"] = "usuuuuussssssssssss",
            ["tables/creature_spawns.tbl"] = "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
            ["tables/creature_appearance.tbl"] = "ukkkuuu",
            ["tables/creaturedisplayinfo.tbl"] = "uuuuussu",
            ["tables/creaturemodeldata.tbl"] = "uissssssssssssssu",
            ["tables/creature_proto.tbl"] = "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuu",
            ["tables/spell_new.tbl"] = "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuuuu",
            ["tables/spell_effect.tbl"] = "uuuuufuuiiiuuuuuuuuuusss",
            ["tables/items.tbl"] = "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuus",
            ["tables/itemdisplayinfo.tbl"] = "uuussssuuu",
            ["tables/quests.tbl"] = "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu",
            ["tables/chat_menu.tbl"] = "uuuuuuuuuuuuuu",
            ["tables/teleport_spot.tbl"] = "usuffffu",
            ["tables/t_bus_route.tbl"] = "uuuuu",
            ["tables/map_graveyard.tbl"] = "uffffuuu",
            ["tables/storage_upgrade.tbl"] = "uuuuuuu",
            ["tables/spell_talent.tbl"] = "uuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuuu",
            ["tables/character_class.tbl"] = "uuuuuuuu",
            ["tables/creature_quest_starter.tbl"] = "uu",
            ["tables/creature_quest_finisher.tbl"] = "uu",
            ["tables/creature_machant_sell_program.tbl"] = "usu",
            ["tables/creature_machant_sell_program_item.tbl"] = "uuuuuuuuuuu",
            ["tables/staticobject_quest_starter.tbl"] = "uu",
            ["tables/staticobject_quest_finisher.tbl"] = "uu",
            ["tables/staticobject_quest_item_binding.tbl"] = "uuuu",
            ["tables/dungeon_reward.tbl"] = "uuuuuus",
        }),
        // Checked before 1.1.0: identical load-bearing shapes, told apart by
        // the Portuguese string tables 1.1.1 adds.
        new BuildSchemas("1.1.1", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tables/map_info.tbl"] = "uuuuufffussuuufuuu",
            ["tables/zones.tbl"] = "iiiuffffffffuuffuuuussssssfffffusiii",
            ["tables/staticobject_spawns.tbl"] = "uuuffffffuufffuf",
            ["tables/staticobject_proto.tbl"] = "uuuuuuufuuuuuuuuuuu",
            ["tables/staticobject_displayinfo.tbl"] = "usuuuuussssssssssss",
            ["tables/creature_spawns.tbl"] = "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
            ["tables/creature_appearance.tbl"] = "ukkkuuu",
            ["tables/creaturedisplayinfo.tbl"] = "uuuuussu",
            ["tables/creaturemodeldata.tbl"] = "uissssssssssssssu",
            ["tables/creature_proto.tbl"] = "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuuuuuuuufuuuuuf",
            ["tables/spell_new.tbl"] = "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuufuuuuuuuuuuuuu",
            ["tables/spell_effect.tbl"] = "uuuuufuuiiiuuuuuuuuuusss",
            ["tables/items.tbl"] = "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuu",
            ["tables/itemdisplayinfo.tbl"] = "uuussssuuu",
            ["tables/quests.tbl"] = "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu",
            ["tables/chat_menu.tbl"] = "uuuuuuuuuuuuuu",
            ["tables/teleport_spot.tbl"] = "usuffffu",
            ["tables/t_bus_route.tbl"] = "uuuu",
            ["tables/map_graveyard.tbl"] = "uffffuuu",
            ["tables/storage_upgrade.tbl"] = "uuuuuuu",
            ["tables/spell_talent.tbl"] = "uuuuuuuuuuuuuusu",
            ["tables/character_class.tbl"] = "uuuuuuuu",
            ["tables/creature_quest_starter.tbl"] = "uu",
            ["tables/creature_quest_finisher.tbl"] = "uu",
            ["tables/creature_machant_sell_program.tbl"] = "usu",
            ["tables/creature_machant_sell_program_item.tbl"] = "uuuuuuuuuuu",
            ["tables/loot.tbl"] = "uuuu",
            ["tables/loot_feature.tbl"] = "uuuuuuuu",
            ["tables/loot_feature_items.tbl"] = "uuufuu",
        },
        PresentTables: ["tables/String_Menu_pt.tbl"]),
        new BuildSchemas("1.1.0", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tables/map_info.tbl"] = "uuuuufffussuuufuuu",
            ["tables/zones.tbl"] = "iiiuffffffffuuffuuuussssssfffffusiii",
            ["tables/staticobject_spawns.tbl"] = "uuuffffffuufffuf",
            ["tables/staticobject_proto.tbl"] = "uuuuuuufuuuuuuuuuuu",
            ["tables/staticobject_displayinfo.tbl"] = "usuuuuussssssssssss",
            ["tables/creature_spawns.tbl"] = "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
            ["tables/creature_appearance.tbl"] = "ukkkuuu",
            ["tables/creaturedisplayinfo.tbl"] = "uuuuussu",
            ["tables/creaturemodeldata.tbl"] = "uissssssssssssssu",
            ["tables/creature_proto.tbl"] = "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuuuuuuuufuuuuuf",
            ["tables/spell_new.tbl"] = "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuufuuuuuuuuuuuuu",
            ["tables/spell_effect.tbl"] = "uuuuufuuiiiuuuuuuuuuusss",
            ["tables/items.tbl"] = "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuu",
            ["tables/itemdisplayinfo.tbl"] = "uuussssuuu",
            ["tables/quests.tbl"] = "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu",
            ["tables/chat_menu.tbl"] = "uuuuuuuuuuuuuu",
            ["tables/teleport_spot.tbl"] = "usuffffu",
            ["tables/t_bus_route.tbl"] = "uuuu",
            ["tables/map_graveyard.tbl"] = "uffffuuu",
            ["tables/storage_upgrade.tbl"] = "uuuuuuu",
            ["tables/spell_talent.tbl"] = "uuuuuuuuuuuuuusu",
            ["tables/character_class.tbl"] = "uuuuuuuu",
            ["tables/creature_quest_starter.tbl"] = "uu",
            ["tables/creature_quest_finisher.tbl"] = "uu",
            ["tables/creature_machant_sell_program.tbl"] = "usu",
            ["tables/creature_machant_sell_program_item.tbl"] = "uuuuuuuuuuu",
            ["tables/loot.tbl"] = "uuuu",
            ["tables/loot_feature.tbl"] = "uuuuuuuu",
            ["tables/loot_feature_items.tbl"] = "uuufuu",
        }),
        new BuildSchemas("1.1.4", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tables/map_info.tbl"] = "uuuuufffussuuufuuu",
            ["tables/zones.tbl"] = "iiiuffffffffuuffuuuussssssfffffusiii",
            ["tables/staticobject_spawns.tbl"] = "uuuffffffuufffuf",
            ["tables/staticobject_proto.tbl"] = "uuuuuuufuuuuuuuuuuuu",
            ["tables/staticobject_displayinfo.tbl"] = "usuuuuussssssssssss",
            ["tables/creature_spawns.tbl"] = "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu",
            ["tables/creature_appearance.tbl"] = "ukkkuuu",
            ["tables/creaturedisplayinfo.tbl"] = "uuuuussu",
            ["tables/creaturemodeldata.tbl"] = "uissssssssssssssu",
            ["tables/creature_proto.tbl"] = "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuu",
            ["tables/spell_new.tbl"] = "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuu",
            ["tables/spell_effect.tbl"] = "uuuuufuuiiiuuuuuuuuuusss",
            ["tables/items.tbl"] = "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuu",
            ["tables/itemdisplayinfo.tbl"] = "uuussssuuu",
            ["tables/quests.tbl"] = "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu",
            ["tables/chat_menu.tbl"] = "uuuuuuuuuuuuuu",
            ["tables/teleport_spot.tbl"] = "usuffffu",
            ["tables/t_bus_route.tbl"] = "uuuuu",
            ["tables/map_graveyard.tbl"] = "uffffuuu",
            ["tables/storage_upgrade.tbl"] = "uuuuuuu",
            ["tables/spell_talent.tbl"] = "uuuuuuuuuuuuuusu",
            ["tables/character_class.tbl"] = "uuuuuuuu",
            ["tables/creature_quest_starter.tbl"] = "uu",
            ["tables/creature_quest_finisher.tbl"] = "uu",
            ["tables/creature_machant_sell_program.tbl"] = "usu",
            ["tables/creature_machant_sell_program_item.tbl"] = "uuuuuuuuuuu",
            ["tables/loot.tbl"] = "uuuu",
            ["tables/loot_feature.tbl"] = "uuuuuuuu",
            ["tables/loot_feature_items.tbl"] = "uuufuu",
        }),
    };

    // The pinned format of one table in one build, or null.
    public static string? FormatFor(string table, string build) =>
        Known.FirstOrDefault(b => b.Build == build) is { } known
        && known.Formats.TryGetValue(table, out var format) ? format : null;

    public static bool Matches(string table, string format, string build) =>
        string.Equals(FormatFor(table, build), format, StringComparison.Ordinal);

    // Name the build whose load-bearing shapes all match, else "unknown".
    // Only tables present in the capture are judged: a partial archive (the
    // synthetic test packages, an unpacked subset) still identifies when
    // everything it holds matches. Row counts are ignored: same build,
    // same shape, different content is still that build.
    public static string IdentifyBuild(TableFingerprint fingerprint)
    {
        var byName = fingerprint.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        foreach (var build in Known)
        {
            // A build may add tables without changing shapes (1.1.1 adds the
            // Portuguese/Russian string tables to the 1.1.0 shapes), so an
            // entry can require tables to be present to tell them apart.
            // A partial archive missing those tables identifies as the older
            // shape generation, which is what its resolvers implement.
            if (build.PresentTables is { Count: > 0 } required
                && required.Any(table => !byName.ContainsKey(table)))
            {
                continue;
            }

            int matched = 0;
            bool mismatch = false;
            foreach (var (table, format) in build.Formats)
            {
                if (!byName.TryGetValue(table, out var got) || got.Error is not null) continue;
                if (!string.Equals(got.Format, format, StringComparison.Ordinal)) { mismatch = true; break; }
                matched++;
            }

            if (!mismatch && matched > 0) return build.Build;
        }

        return "unknown";
    }

    // One line per load-bearing table whose shape differs from the named
    // build, for the import log. Empty means the import is reading exactly
    // the shapes its column layouts were written against.
    public static IReadOnlyList<string> DescribeDrift(TableFingerprint fingerprint, string build)
    {
        var known = Known.FirstOrDefault(b => b.Build == build);
        if (known is null) return [$"build '{build}' has no pinned schemas."];
        var byName = fingerprint.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var drift = new List<string>();
        foreach (var table in LoadBearingTables)
        {
            if (!known.Formats.TryGetValue(table, out var want)) continue;
            if (!byName.TryGetValue(table, out var got) || got.Error is not null)
            {
                drift.Add($"{table}: missing or unreadable (import expects format \"{want}\").");
            }
            else if (!string.Equals(got.Format, want, StringComparison.Ordinal))
            {
                drift.Add($"{table}: format \"{got.Format}\" differs from {build} \"{want}\".");
            }
        }

        return drift;
    }
}
