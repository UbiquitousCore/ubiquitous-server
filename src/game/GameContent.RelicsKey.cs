using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed partial record GameContent
{
    public const ushort RelicsKeyMap = 15;

    // The swamp's two dungeon-gate objects in the operator's spawn tables: 206 on map 1 at the mouth, 205 on
    // map 15 inside. Each gate's rectangle is laid round its object the way the prison's are. This and the
    // members below are computed, not stored, because `Development` reads them while its own initialiser runs.
    public static IReadOnlyList<MapGateDefinition> RelicsKeyGates =>
    [
        new(SourceMapId: 1, SourceMinX: -895, SourceMaxX: -883, SourceMinZ: -1936, SourceMaxZ: -1920,
            DestinationMapId: RelicsKeyMap, DestinationX: -221, DestinationY: 39.5f, DestinationZ: 219,
            DestinationYaw: 2.2f, IsDungeonEntrance: true),
        new(SourceMapId: RelicsKeyMap, SourceMinX: -233, SourceMaxX: -226, SourceMinZ: 220, SourceMaxZ: 227,
            DestinationMapId: 1, DestinationX: -880, DestinationY: -6.5f, DestinationZ: -1912, DestinationYaw: 0f,
            IsDungeonExit: true),
    ];

    // Own choice, to be placed in play: none of these points is in the operator's tables. Each stands on a spot a
    // creature in the swamp's own spawn table stands on: the keeper beside the way in, the ritual where the Swamp
    // Guardians wait, and the creatures that come for it at the crocodiles' spots.
    public static RitualSiteDefinition RelicsKeyRitualSite => new(
        RelicsKeyMap, "ritual.relicsKey", GossipScripts.StartsRitual,
        KeeperEntry: 2659, CastingKeeperEntry: 2660,
        KeeperStart: new MapPoint(-212f, 39.5f, 214f),
        Site: new MapPoint(-132f, 40.9f, 147f),
        WaveGates: [new MapPoint(-156.5f, 38.9f, 169.4f), new MapPoint(-123f, 39f, 184.6f), new MapPoint(-150.8f, 39.4f, 172.9f)]);

    public static LegendCounterparts RelicsKeyLegend => new(
        Creatures: new Dictionary<uint, uint>
        {
            [2619] = 2651,
            [2626] = 2652,
            [2620] = 2654,
            [2621] = 2655,
            [2623] = 2636,
            [2624] = 2637,
            [2625] = 2638,
            [2627] = 2639,
            [2628] = 2640,
            [2629] = 2641,
            [2630] = 2642,
            [2631] = 2643,
            [2632] = 2644,
            [2633] = 2645,
            [2634] = 2646,
            [2635] = 2647,
            [2648] = 2649,
            [2650] = 2653,
            [2659] = 2661,
            [2660] = 2662,
        },
        Spells: new Dictionary<uint, uint>
        {
            [2370] = 2438,
            [2371] = 2439,
            [2372] = 2440,
            [2373] = 2417,
            [2374] = 2418,
            [2375] = 2419,
            [2379] = 2441,
            [2380] = 2442,
            [2381] = 2443,
            [2382] = 2444,
            [2383] = 2445,
            [2384] = 2446,
            [2385] = 2447,
            [2386] = 2448,
            [2395] = 2518,
            [2401] = 2519,
            [2402] = 2520,
            [2403] = 2521,
            [2404] = 2522,
            [2405] = 2523,
            [2406] = 2524,
            [2408] = 2525,
            [2409] = 2526,
            [2410] = 2527,
            [2412] = 2528,
            [2413] = 2529,
            [2414] = 2530,
            [2415] = 2531,
            [2416] = 2532,
            [2670] = 2671,
        });

    public static IReadOnlyDictionary<uint, string> RelicsKeyScripts => new Dictionary<uint, string>
    {
        [2619] = "boss.hathraad",
        [2620] = "boss.kermode",
        [2621] = "boss.alrRahjil",
        [2626] = "swamp.hathraadMirror",
        [2659] = "npc.tharrath",
        [2660] = "npc.tharrath",
        [2623] = "swamp.giantTortoise",
        [2624] = "swamp.giantTailCrocodile",
        [2625] = "swamp.guardian",
        [2627] = "swamp.sonOfTheEarth",
        [2628] = "swamp.sonOfTheLake",
        [2629] = "swamp.rottenSonOfTheLake",
        [2630] = "swamp.ghostlyHunter",
        [2631] = "swamp.ghostlyGladiator",
        [2632] = "swamp.ghostlyMage",
        [2633] = "swamp.ghostlyPriest",
        [2634] = "swamp.lavaSkinBurner",
        [2635] = "swamp.rockWingsAssassin",
        [2648] = "swamp.flameStriker",
    };

    public static DungeonSummonDefinition RelicsKeyKeeper => new(
        RelicsKeyMap, 2659, After: [],
        RelicsKeyRitualSite.KeeperStart.X, RelicsKeyRitualSite.KeeperStart.Y, RelicsKeyRitualSite.KeeperStart.Z);

    // How far the guide says each of these reaches: Kermode's shouts about 30 metres, Hathraad's Tempest a wide
    // radius, Wraith Blast about 20, Pounding Glow about 8.
    public static IReadOnlyDictionary<uint, float> RelicsKeyReach => new Dictionary<uint, float>
    {
        [2370] = 30f,
        [2371] = 30f,
        [2438] = 30f,
        [2439] = 30f,
        [2375] = 30f,
        [2419] = 30f,
        [2381] = 20f,
        [2443] = 20f,
        [2414] = 8f,
        [2530] = 8f,
    };
}
