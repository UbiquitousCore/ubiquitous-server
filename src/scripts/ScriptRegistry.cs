using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Scripts;

public sealed class ScriptRegistry
{
    private static readonly CreatureScript[] Known =
    [
        DefaultCreatureScript.Instance,
        GuardScript.Instance,
        DungeonInmateScript.Instance,
        TrainingDummyScript.Instance,
        ExecutionerScript.Instance,
        ArielScript.Instance,
        CosmoScript.Instance,
        AndareScript.Instance,
        HathraadScript.Instance,
        KermodeScript.Instance,
        AlrRahjilScript.Instance,
        TharrathScript.Instance,
        .. PrisonInmates.All,
        .. RelicsKeyCreatures.All,
    ];

    public static readonly IReadOnlyDictionary<string, CreatureScript> Catalogue =
        Known.ToDictionary(script => script.Name, StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<uint, string> _namedByEntry;
    private readonly InstanceScript _dungeon;
    private readonly IReadOnlySet<ushort> _dungeonMaps;

    public ScriptRegistry(
        IReadOnlyList<DungeonDefinition> dungeonRules,
        IReadOnlyDictionary<uint, string>? namedByEntry = null)
    {
        _namedByEntry = WithLegendCounterparts(namedByEntry ?? new Dictionary<uint, string>(), dungeonRules);
        _dungeon = new AuthoredInstanceScript(dungeonRules);
        _dungeonMaps = dungeonRules.Select(rule => rule.MapId).ToHashSet();
    }

    private static IReadOnlyDictionary<uint, string> WithLegendCounterparts(
        IReadOnlyDictionary<uint, string> named, IReadOnlyList<DungeonDefinition> dungeonRules)
    {
        var all = new Dictionary<uint, string>(named);
        foreach (var legend in dungeonRules.Select(rule => rule.Legend).OfType<LegendCounterparts>())
        {
            foreach (var (authored, counterpart) in legend.Creatures)
            {
                if (named.TryGetValue(authored, out var script)) all.TryAdd(counterpart, script);
            }
        }

        return all;
    }

    public bool IsDungeonMap(ushort mapId) => _dungeonMaps.Contains(mapId);

    public static Ritual? RitualFor(string name) => name switch
    {
        "ritual.relicsKey" => RelicsKeyRitual.Create(),
        _ => null,
    };

    public InstanceScript ForMap(ushort mapId) => _dungeon;

    public CreatureScript For(CreatureDefinition? creature, bool onDungeonMap, bool advertisesQuests)
    {
        if (creature is not null
            && _namedByEntry.TryGetValue(creature.EntryId, out var named)
            && Catalogue.TryGetValue(named, out var bound))
            return bound;
        if (CreatureExtraFlags.IsTrainingDummy(creature)) return TrainingDummyScript.Instance;
        if (onDungeonMap) return DungeonInmateScript.Instance;
        if (LooksLikeAGuard(creature, advertisesQuests)) return GuardScript.Instance;
        return DefaultCreatureScript.Instance;
    }

    private static bool LooksLikeAGuard(CreatureDefinition? creature, bool advertisesQuests)
    {
        if (creature is null) return false;
        if (FactionStance.Resolve(creature.FactionType) != UnitFactionTypes.Townsfolk) return false;
        return creature.NameKey.Contains("guard", StringComparison.OrdinalIgnoreCase) || advertisesQuests;
    }
}
