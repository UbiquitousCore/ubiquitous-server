using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed partial record GameContent(
    IReadOnlyDictionary<uint, NpcDefinition> Npcs,
    IReadOnlyDictionary<uint, CreatureDefinition> Creatures,
    IReadOnlyList<QuestDefinition> Quests,
    IReadOnlyDictionary<uint, QuestRewardDefinition> QuestRewards,
    IReadOnlyDictionary<uint, TeleportStopDefinition> Stops,
    IReadOnlyList<TravelRouteDefinition> Routes,
    IReadOnlyDictionary<uint, IReadOnlyList<VendorTab>> VendorTabs,
    int StartingCurrency,
    IReadOnlyDictionary<uint, SpellDefinition> Spells,
    IReadOnlyList<uint> StartingSpells,
    IReadOnlyList<StartingItemStack> StartingItems,
    IReadOnlyList<StorageUpgradeDefinition> StorageUpgrades,
    IReadOnlySet<uint> BankBoxEntries,
    IReadOnlyDictionary<uint, ItemDefinition> Items,
    IReadOnlyList<StaticObjectQuestBindingDefinition>? QuestStartersByStaticObject = null,
    IReadOnlyList<StaticObjectQuestBindingDefinition>? QuestFinishersByStaticObject = null,
    IReadOnlyList<StaticObjectItemBindingDefinition>? QuestItemBindingsByStaticObject = null,
    IReadOnlyList<CreatureQuestBindingDefinition>? QuestFinishersByCreature = null,
    IReadOnlyDictionary<ushort, MapDefinition>? Maps = null,
    IReadOnlySet<uint>? MailBoxEntries = null,
    IReadOnlyDictionary<uint, uint>? StaticObjectTypesByEntry = null,
    IReadOnlyDictionary<uint, CreatureLootTableDefinition>? LootTables = null,
    IReadOnlyList<MapGraveyardDefinition>? Graveyards = null,
    IReadOnlyList<MapGateDefinition>? MapGates = null,
    IReadOnlyList<DungeonDefinition>? Dungeons = null,
    IReadOnlyList<DungeonBarrierDefinition>? DungeonBarriers = null,
    IReadOnlyList<DungeonSummonDefinition>? DungeonSummons = null,
    IReadOnlyList<StaticObjectSpawnDefinition>? HiddenStaticObjects = null,
    IReadOnlyDictionary<uint, string>? CreatureScripts = null,
    IReadOnlyList<StartingActionButton>? StartingActionBar = null,
    IReadOnlyDictionary<uint, WaypointRouteDefinition>? WaypointRoutes = null,
    IReadOnlyDictionary<uint, GossipMenuDefinition>? GossipMenus = null,
    IReadOnlyList<TalentTreeDefinition>? TalentTrees = null)
{
    public IReadOnlyList<TalentTreeDefinition> TalentTrees { get; init; } = TalentTrees ?? [];

    public IReadOnlyDictionary<uint, GossipMenuDefinition> GossipMenus { get; init; } =
        GossipMenus ?? new Dictionary<uint, GossipMenuDefinition>();

    public IReadOnlyList<StartingActionButton> StartingActionBar { get; init; } = StartingActionBar ?? [];

    public IReadOnlyList<StaticObjectQuestBindingDefinition> QuestStartersByStaticObject { get; init; } =
        QuestStartersByStaticObject ?? [];

    public IReadOnlyList<StaticObjectQuestBindingDefinition> QuestFinishersByStaticObject { get; init; } =
        QuestFinishersByStaticObject ?? [];

    public IReadOnlyList<StaticObjectItemBindingDefinition> QuestItemBindingsByStaticObject { get; init; } =
        QuestItemBindingsByStaticObject ?? [];

    public IReadOnlyList<CreatureQuestBindingDefinition> QuestFinishersByCreature { get; init; } =
        QuestFinishersByCreature ?? [];

    public IReadOnlyDictionary<ushort, MapDefinition> Maps { get; init; } =
        Maps ?? new Dictionary<ushort, MapDefinition>();

    public IReadOnlySet<uint> MailBoxEntries { get; init; } = MailBoxEntries ?? new HashSet<uint>();

    public IReadOnlySet<uint> SummonStoneEntries { get; init; } = new HashSet<uint>();

    public IReadOnlyDictionary<uint, uint> StaticObjectTypesByEntry { get; init; } =
        StaticObjectTypesByEntry ?? new Dictionary<uint, uint>();

    public IReadOnlyDictionary<uint, CreatureLootTableDefinition> LootTables { get; init; } =
        LootTables ?? new Dictionary<uint, CreatureLootTableDefinition>();

    public IReadOnlyList<MapGraveyardDefinition> Graveyards { get; init; } = Graveyards ?? [];

    public IReadOnlyList<MapGateDefinition> MapGates { get; init; } = MapGates ?? [];

    public IReadOnlyList<DungeonDefinition> Dungeons { get; init; } = Dungeons ?? [];

    public IReadOnlyList<DungeonBarrierDefinition> DungeonBarriers { get; init; } = DungeonBarriers ?? [];
    public IReadOnlyList<DungeonSummonDefinition> DungeonSummons { get; init; } = DungeonSummons ?? [];

    public IReadOnlyList<StaticObjectSpawnDefinition> HiddenStaticObjects { get; init; } =
        HiddenStaticObjects ?? [];

    public IReadOnlyDictionary<uint, string> CreatureScripts { get; init; } =
        CreatureScripts ?? new Dictionary<uint, string>();

    public IReadOnlySet<int> PlayableRaces { get; init; } = PlayerRaceIds.All;

    public IReadOnlyList<PlayerLookDefinition> PlayerLooks { get; init; } = [];

    public Ubiquitous.World.ITerrainHeightSource? Terrain { get; init; }

    public IReadOnlyDictionary<uint, int> SpellStackLimits { get; init; } = new Dictionary<uint, int>();

    public IReadOnlyDictionary<uint, WaypointRouteDefinition> WaypointRoutes { get; init; } =
        WaypointRoutes ?? new Dictionary<uint, WaypointRouteDefinition>();
}
