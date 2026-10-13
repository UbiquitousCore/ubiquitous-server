using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Modules.Playerbots;

// What a player learns from the client's own tables: which creatures a quest
// wants dead or drops its item, which objects to use, and what a spell does.
// A free or paid teleport an NPC's conversation offers: talk to `NpcEntry` where it stands
// and pick `Choice` on page `MenuId`.
public sealed record NpcTrip(uint NpcEntry, Ubiquitous.Common.Position At, uint MenuId, int Choice, ushort ToMap, Ubiquitous.Common.Position ToAt, int Cost);

public sealed class BotKnowledge
{
    private readonly Dictionary<uint, QuestDefinition> _quests;
    private readonly Dictionary<uint, HashSet<uint>> _droppersByItem = [];

    private const int WornSlots = 16;
    private readonly GameContent _content;
    private readonly TravelService _buses;

    private readonly ILookup<uint, CreatureSpawnDefinition> _spawnsByEntry;

    public ItemDefinition? QuestStarterItem(int entry) => Item(entry) is { QuestId: > 0 } item ? item : null;

    public bool StartsQuests(uint staticObjectEntry) => _questObjects.Contains(staticObjectEntry);

    private readonly HashSet<uint> _questObjects;

    // Whether a body put down here could walk anywhere: a stop or a teleport landing off the
    // walkable ground leaves a bot standing there for good.
    public bool StandsOnMainGround(ushort mapId, Ubiquitous.Common.Position at) => _standsOnMainGround(mapId, at);

    private readonly Func<ushort, Ubiquitous.Common.Position, bool> _standsOnMainGround;

    public BotKnowledge(GameContent content, IReadOnlyList<CreatureSpawnDefinition>? spawns = null,
        Func<ushort, Ubiquitous.Common.Position, bool>? standsOnMainGround = null)
    {
        _standsOnMainGround = standsOnMainGround ?? ((_, _) => true);
        _spawnsByEntry = (spawns ?? []).ToLookup(spawn => spawn.CreatureEntryId);
        _questObjects = content.QuestStartersByStaticObject.Select(binding => binding.StaticObjectEntry).ToHashSet();
        _content = content;
        _buses = new TravelService(content.Stops, content.Routes);
        _quests = content.Quests.GroupBy(quest => quest.Id).ToDictionary(group => group.Key, group => group.First());
        Spells = content.Spells;
        foreach (var table in content.LootTables.Values)
        {
            foreach (var item in table.Groups.SelectMany(group => group.Items))
            {
                if (!_droppersByItem.TryGetValue(item.ItemId, out var droppers)) _droppersByItem[item.ItemId] = droppers = [];
                droppers.Add(table.CreatureEntryId);
            }
        }
    }

    public IReadOnlyDictionary<uint, SpellDefinition> Spells { get; }

    public IReadOnlyList<MapGateDefinition> MapGates => _content.MapGates;

    public IEnumerable<TalentTreeDefinition> TalentTreesFor(int playerClass) =>
        _content.TalentTrees.Where(tree => tree.ClassId == playerClass).OrderBy(tree => tree.Id);

    public ItemDefinition? Item(int entry) => entry > 0 ? _content.Items.GetValueOrDefault((uint)entry) : null;

    // The worn slots an item can go in, as the server's own slot rules say.
    public IReadOnlyList<int> SlotsFor(ItemDefinition item) =>
        [.. Enumerable.Range(0, WornSlots).Where(slot => EquipSlotRules.IsValidSlot(item.InventoryType, slot))];

    public bool CanWear(ItemDefinition item, int playerClass, int level) =>
        item.RequiredLevel <= level && ItemClassMask.Allows(item.AllowableClassMask, playerClass) && SlotsFor(item).Count > 0;

    // How much a character of this class gains from wearing the item. The weights are this project's own.
    public static float Worth(ItemDefinition? item, int playerClass)
    {
        if (item is null) return 0f;
        var worth = (float)(item.Armor ?? 0);
        foreach (var stat in item.Stats ?? [])
        {
            var weight = stat.Attribute == MainAttribute(playerClass) ? 2f
                : stat.Attribute == AttributeIds.Stamina ? 1.5f : 1f;
            worth += stat.Amount * weight;
        }

        if (playerClass != PlayerClassIds.Mage && item is { MaxDamage: > 0, SpeedMs: > 0 })
            worth += ((item.MinDamage ?? 0) + item.MaxDamage.Value) / 2f / (item.SpeedMs.Value / 1000f) * 4f;
        return worth;
    }

    private static int MainAttribute(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => AttributeIds.Strength,
        PlayerClassIds.Ranger => AttributeIds.Agility,
        _ => AttributeIds.Wisdom,
    };

    public IEnumerable<TeleportStopDefinition> BusStopsOn(ushort mapId) =>
        _content.Stops.Values.Where(stop => stop.Kind == TeleportStopKind.BusStop && stop.MapId == mapId);

    public IEnumerable<TravelQuote> FaresFrom(TeleportStopDefinition stop) =>
        _content.Stops.Values
            .Where(other => other.Kind == TeleportStopKind.BusStop && other.Id != stop.Id)
            .Select(other => _buses.Quote(stop.Id, other.Id, stop.MapId, stop.X, stop.Y, stop.Z))
            .OfType<TravelQuote>();

    public QuestDefinition? Quest(uint id) => _quests.GetValueOrDefault(id);

    // Where creatures a player could fight stand around a place, by their median level; null where none do.
    public int? LevelAround(ushort mapId, Ubiquitous.Common.Position at, float radius)
    {
        var levels = _spawnsByEntry.SelectMany(group => group)
            .Where(spawn => spawn.MapId == mapId
                && (spawn.X - at.X) * (spawn.X - at.X) + (spawn.Z - at.Z) * (spawn.Z - at.Z) <= radius * radius)
            .Select(spawn => _content.Creatures.GetValueOrDefault(spawn.CreatureEntryId))
            .Where(creature => creature is { Level: > 0 } && creature.FactionType is null or UnitFactionTypes.Hostile)
            .Select(creature => creature!.Level)
            .Order()
            .ToList();
        return levels.Count == 0 ? null : levels[levels.Count / 2];
    }

    // Where a character made at a higher level starts: a bus stop whose surroundings come nearest
    // its level, any of those about as near, as a player levelled there would be found.
    public (ushort MapId, Ubiquitous.Common.Position At)? StartFor(int level, Random random, float radius, int slack)
    {
        if (level <= CharacterCreation.StartingLevel) return null;
        var fits = _content.Stops.Values
            .Where(stop => stop.Kind == TeleportStopKind.BusStop
                && StandsOnMainGround(stop.MapId, new Ubiquitous.Common.Position(stop.X, stop.Y, stop.Z)))
            .Select(stop => (Stop: stop, Level: LevelAround(stop.MapId, new Ubiquitous.Common.Position(stop.X, stop.Y, stop.Z), radius)))
            .Where(fit => fit.Level is not null)
            .Select(fit => (fit.Stop, Misfit: Math.Abs(fit.Level!.Value - level)))
            .ToList();
        if (fits.Count == 0) return null;
        var best = fits.Min(fit => fit.Misfit);
        var good = fits.Where(fit => fit.Misfit <= best + slack).OrderBy(fit => fit.Stop.Id).ToList();
        var stop = good[random.Next(good.Count)].Stop;
        return (stop.MapId, new Ubiquitous.Common.Position(stop.X, stop.Y, stop.Z));
    }

    // Every teleport a conversation on this map sells or gives, found by asking the server's own
    // conversation rule each choice on every page an NPC's greeting leads to.
    public IReadOnlyList<NpcTrip> NpcTripsOn(ushort mapId)
    {
        if (_npcTrips.TryGetValue(mapId, out var known)) return known;
        var trips = new List<NpcTrip>();
        var gossip = new GossipService();
        foreach (var spawn in _spawnsByEntry.SelectMany(group => group).Where(spawn => spawn.MapId == mapId))
        {
            if (!_content.Npcs.TryGetValue(spawn.CreatureEntryId, out var npc) || npc.GreetingMenuId == 0) continue;
            foreach (var menu in PagesFrom(npc.GreetingMenuId))
            {
                for (var choice = 1; choice <= GossipService.ChoiceSlots; choice++)
                {
                    var pick = gossip.Pick(new GossipRequest("", menu, choice), npc.EntryId, _content.Npcs, _content.GossipMenus, _content.Stops);
                    if (pick is { Kind: GossipPickKind.Teleport, Teleport: { } to })
                        trips.Add(new NpcTrip(npc.EntryId, new Ubiquitous.Common.Position(spawn.X, spawn.Y, spawn.Z), menu, choice,
                            to.MapId, new Ubiquitous.Common.Position(to.X, to.Y, to.Z), to.Cost));
                }
            }
        }

        _npcTrips[mapId] = trips;
        return trips;
    }

    private IEnumerable<uint> PagesFrom(uint greeting)
    {
        var seen = new HashSet<uint> { greeting };
        var queue = new Queue<uint>([greeting]);
        while (queue.TryDequeue(out var id))
        {
            yield return id;
            if (!_content.GossipMenus.TryGetValue(id, out var menu)) continue;
            foreach (var next in menu.SubMenus.Where(seen.Add)) queue.Enqueue(next);
        }
    }

    private readonly Dictionary<ushort, IReadOnlyList<NpcTrip>> _npcTrips = [];

    public IEnumerable<Ubiquitous.Common.Position> SpawnsOf(ushort mapId, IEnumerable<uint> creatures) =>
        creatures.SelectMany(entry => _spawnsByEntry[entry])
            .Where(spawn => spawn.MapId == mapId)
            .Select(spawn => new Ubiquitous.Common.Position(spawn.X, spawn.Y, spawn.Z));

    // The spells a quest wants cast; a bot without one of them cannot finish it.
    public IEnumerable<uint> SpellsWanted(uint questId) =>
        Quest(questId)?.Objectives
            .Where(objective => objective is { Kind: QuestObjectiveKind.Triggered, ObjectiveSpell: not 0, ObjectiveObject: 0 })
            .Select(objective => objective.ObjectiveSpell) ?? [];


    // The creature entries, object entries and spells that would move an unfinished
    // objective. A spell objective with no object is met by casting the spell (the
    // server counts each target once), which class quests ask for on a training dummy.
    public (HashSet<uint> Creatures, HashSet<uint> Objects, HashSet<uint> Spells) WantedFor(QuestState quest)
    {
        var creatures = new HashSet<uint>();
        var objects = new HashSet<uint>();
        var spells = new HashSet<uint>();
        if (Quest(quest.Id) is not { } definition) return (creatures, objects, spells);
        foreach (var objective in definition.Objectives)
        {
            var state = quest.Objectives.FirstOrDefault(o => o.Index == objective.Index);
            if (state is not null && state.Done >= state.Goal) continue;
            if (objective.ObjectiveCreature != 0) creatures.Add(objective.ObjectiveCreature);
            if (objective.ObjectiveObject != 0) objects.Add(objective.ObjectiveObject);
            else if (objective is { Kind: QuestObjectiveKind.Triggered, ObjectiveSpell: not 0 }) spells.Add(objective.ObjectiveSpell);
            if (objective.ObjectiveItem != 0 && _droppersByItem.TryGetValue(objective.ObjectiveItem, out var droppers))
                creatures.UnionWith(droppers);
        }

        return (creatures, objects, spells);
    }

    public IEnumerable<uint> ObjectiveItems(uint questId) =>
        Quest(questId)?.Objectives.Where(objective => objective.ObjectiveItem != 0).Select(objective => objective.ObjectiveItem) ?? [];

    // A spell cast on the caster or a friend, or with no range at all, needs no creature to aim at.
    public bool AimsAtSelf(uint spellId) =>
        Spells.TryGetValue(spellId, out var spell)
        && (spell.EffectTarget == SpellEffectTargets.Self || SpellEffectTargets.IsFriendly(spell.EffectTarget)
            || spell is { EffectTarget: 0, MaxRange: <= 0f });

    public bool IsHarmful(uint spellId) =>
        Spells.TryGetValue(spellId, out var spell)
        && (spell.EffectDamage is > 0 || spell.AuraPeriodicDamage is > 0 || spell.EffectMeleeWeaponHit || spell.EffectRangedWeaponHit)
        && spell.EffectTarget != SpellEffectTargets.Self;

    public bool HealsOthers(uint spellId) => Spells.TryGetValue(spellId, out var spell) && spell.EffectHeal is > 0;

    public bool IsHeal(uint spellId) =>
        Spells.TryGetValue(spellId, out var spell) && (spell.EffectHeal is > 0 || spell.AuraPeriodicHeal is > 0);
}
