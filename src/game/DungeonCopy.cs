using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed class DungeonCopy
{
    public static readonly DungeonCopy Normal = new(DungeonMode.Normal,
        new Dictionary<uint, uint>(), new Dictionary<uint, uint>());

    private readonly IReadOnlyDictionary<uint, uint> _creatures;
    private readonly IReadOnlyDictionary<uint, uint> _spells;
    private readonly IReadOnlyDictionary<uint, uint> _authoredCreatures;

    private DungeonCopy(
        DungeonMode mode, IReadOnlyDictionary<uint, uint> creatures, IReadOnlyDictionary<uint, uint> spells)
    {
        Mode = mode;
        _creatures = creatures;
        _spells = spells;
        _authoredCreatures = creatures.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    public DungeonMode Mode { get; }

    public static DungeonCopy For(IEnumerable<DungeonDefinition> dungeons, ushort mapId, DungeonMode mode)
    {
        if (mode == DungeonMode.Normal) return Normal;

        var creatures = new Dictionary<uint, uint>();
        var spells = new Dictionary<uint, uint>();
        foreach (var legend in LegendsOn(dungeons, mapId))
        {
            foreach (var (authored, counterpart) in legend.Creatures) creatures[authored] = counterpart;
            foreach (var (authored, counterpart) in legend.Spells) spells[authored] = counterpart;
        }

        return creatures.Count == 0 && spells.Count == 0 ? Normal : new DungeonCopy(mode, creatures, spells);
    }

    public static bool OffersLegend(IEnumerable<DungeonDefinition> dungeons, ushort mapId) =>
        LegendsOn(dungeons, mapId).Any();

    public uint CreatureEntry(uint authoredEntry) =>
        _creatures.TryGetValue(authoredEntry, out var counterpart) ? counterpart : authoredEntry;

    public uint Spell(uint authoredSpell) =>
        _spells.TryGetValue(authoredSpell, out var counterpart) ? counterpart : authoredSpell;

    public uint AuthoredEntry(uint entry) =>
        _authoredCreatures.TryGetValue(entry, out var authored) ? authored : entry;

    public CreatureSpawnDefinition Spawn(CreatureSpawnDefinition authored) =>
        authored with { CreatureEntryId = CreatureEntry(authored.CreatureEntryId) };

    private static IEnumerable<LegendCounterparts> LegendsOn(IEnumerable<DungeonDefinition> dungeons, ushort mapId) =>
        dungeons.Where(dungeon => dungeon.MapId == mapId && dungeon.Legend is not null).Select(dungeon => dungeon.Legend!);
}
