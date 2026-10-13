using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record MiningOre(uint NodeEntry, uint ItemId);

public sealed record MiningOreWeight(MiningOre Ore, int Weight);

public static class MiningOres
{
    public static readonly MiningOre Bronze = new(132, 5192);
    public static readonly MiningOre Marcasite = new(167, 5193);
    public static readonly MiningOre PrimalIron = new(168, 5194);
    public static readonly MiningOre LightSilver = new(198, 5195);
    public static readonly MiningOre MattedGold = new(199, 5197);
    public static readonly MiningOre MagicalBlood = new(200, 5198);

    public static readonly IReadOnlyList<MiningOre> All =
        [Bronze, Marcasite, PrimalIron, LightSilver, MattedGold, MagicalBlood];

    // TODO: Verify per-map ores and weights in play; these are placeholders.
    private static readonly IReadOnlyList<MiningOreWeight> StarterZone =
        [new(Bronze, 70), new(Marcasite, 30)];

    private static readonly IReadOnlyList<MiningOreWeight> MiddleZone =
        [new(PrimalIron, 60), new(LightSilver, 40)];

    private static readonly IReadOnlyList<MiningOreWeight> DeepZone =
        [new(MattedGold, 70), new(MagicalBlood, 30)];

    public static MiningOre? ByNodeEntry(uint entry) => All.FirstOrDefault(ore => ore.NodeEntry == entry);

    public static IReadOnlyList<MiningOreWeight> ForMap(ushort mapId) => mapId switch
    {
        MiningMaps.MiddleLand => MiddleZone,
        MiningMaps.SailenPalace or MiningMaps.CaligulaSkygarden => DeepZone,
        _ => StarterZone,
    };
}

public static class MiningMaps
{
    public const ushort MiddleLand = 4;
    public const ushort SailenPalace = 19;
    public const ushort CaligulaSkygarden = 20;
}

public static class MiningRules
{
    public const uint LocationEntry = 52;

    public const uint GatherSpellId = 23;

    public const int DefaultCastTimeMs = 3500;

    // TODO: Verify ore per node, active share and recovery time in play; all are provisional.
    public const int OrePerNode = 1;

    public const double ActiveShare = 0.25;

    public static readonly TimeSpan LocationCooldown = TimeSpan.FromMinutes(3);

    // TODO: Verify Mendel mining racial bonus; taken from the wiki, not seen in play.
    public const double MendelCastTimeFactor = 0.5;

    public static int ActiveCount(int locations) =>
        locations <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(locations * ActiveShare), 1, locations);

    public static TimeSpan CastTime(SpellDefinition? gatherSpell, int race)
    {
        var milliseconds = gatherSpell is { CastTimeMs: > 0 } ? gatherSpell.CastTimeMs : DefaultCastTimeMs;
        var factor = race == PlayerRaceIds.Mendel ? MendelCastTimeFactor : 1.0;
        return TimeSpan.FromMilliseconds(milliseconds * factor);
    }
}
