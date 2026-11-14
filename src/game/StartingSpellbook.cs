using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record ClassStartingSpells(uint AutoShoot, IReadOnlyList<uint> ClassSpells, uint Opener);

// What a new character of a build other than 1.1.4 knows, read from that build's own tables.
// 1.1.4's spellbooks were seen in play and stay as `StartingLoadouts.All` lists them.
public sealed record StartingSpellbook(
    uint AutoAttack,
    uint SwitchTarget,
    IReadOnlyList<uint> Gathering,
    uint MiningGather,
    IReadOnlyDictionary<int, uint> RacialSpells,
    IReadOnlyDictionary<int, ClassStartingSpells> Classes)
{
    public const string AutoAttackName = "Auto Attack";
    public const string AutoShootName = "Auto Shoot";
    public const string SwitchTargetName = "Switch Target";

    // Own choice: a gather spell counts as everyone's when more than one static-object type in a
    // hundred names it; in 4.2.2 that is spells 1, 2 and 3, and the next is named by 7 of 1,113.
    public const int GatheringShareOfTypes = 100;

    // Own choice: a class chain's first rank comes with the class when its second rank is taught
    // no more than this many levels above the starting level. At level 1 it gives each 4.2.2 class
    // two or three spells, and a mage the same three 1.1.4 gives.
    public const int FirstRankLead = 3;

    public static readonly IReadOnlyDictionary<int, string> RidingPermitNames = new Dictionary<int, string>
    {
        [PlayerRaceIds.Human] = "Riding Permit for Humans",
        [PlayerRaceIds.Elf] = "Riding Permit for Elves",
        [PlayerRaceIds.Orc] = "Riding Permit for Orcs",
        [PlayerRaceIds.Undead] = "Riding Permit for Undead",
        [PlayerRaceIds.Mendel] = "Riding Permit for Mendel",
    };

    public IReadOnlyList<uint> SpellsFor(int race, int playerClass)
    {
        var start = Classes[playerClass];
        IReadOnlyList<uint> racial = RacialSpells.TryGetValue(race, out var permit) ? [permit] : [];
        return [.. start.ClassSpells, AutoAttack, start.AutoShoot, SwitchTarget, .. Gathering, .. racial];
    }

    public static StartingSpellbook Read(
        IReadOnlyDictionary<uint, SpellDefinition> spells,
        IReadOnlyDictionary<uint, ItemDefinition> items,
        IReadOnlyList<StaticObjectTypeDefinition> staticObjectTypes,
        IReadOnlyDictionary<int, int> startingLevelByClass)
    {
        var missing = new List<string>();
        uint Named(string name)
        {
            var found = spells.Values.Where(spell => spell.NameKey == name).OrderBy(spell => spell.Id).FirstOrDefault();
            if (found is null) missing.Add($"no spell named \"{name}\"");
            return found?.Id ?? 0;
        }

        var autoAttack = Named(AutoAttackName);
        var switchTarget = Named(SwitchTargetName);
        var gathering = EveryonesGathering(staticObjectTypes);
        if (gathering.Count == 0) missing.Add("no gather spell most static objects name");
        var miningGather = staticObjectTypes.FirstOrDefault(type => type.Entry == MiningOres.Bronze.NodeEntry)?.GatherSpellId ?? 0;
        if (miningGather == 0) missing.Add($"no gather spell on ore node {MiningOres.Bronze.NodeEntry}");

        var racial = RidingPermitNames
            .Select(pair => (Race: pair.Key, Spell: spells.Values.Where(spell => spell.NameKey == pair.Value)
                .OrderBy(spell => spell.Id).FirstOrDefault()))
            .Where(found => found.Spell is not null)
            .ToDictionary(found => found.Race, found => found.Spell!.Id);

        var autoShoots = spells.Values
            .Where(spell => spell.NameKey == AutoShootName && spell.WornSlotNeeded == EquipmentSlots.Ranged)
            .OrderBy(spell => spell.Id)
            .ToList();
        if (autoShoots.Count == 0) missing.Add($"no ranged spell named \"{AutoShootName}\"");

        var classes = new Dictionary<int, ClassStartingSpells>();
        var playable = startingLevelByClass.Keys.ToHashSet();
        foreach (var (playerClass, level) in startingLevelByClass)
        {
            var classSpells = ClassSpellsAt(items, spells, playerClass, level, playable);
            if (classSpells.Count == 0) missing.Add($"class {playerClass} has no spell a lesson of its own teaches");
            var autoShoot = autoShoots
                .OrderByDescending(spell => RangedWeaponsCovered(spell, items.Values, playerClass))
                .ThenBy(spell => spell.Id)
                .FirstOrDefault()?.Id ?? 0;
            classes[playerClass] = new ClassStartingSpells(autoShoot, classSpells, classSpells.FirstOrDefault());
        }

        if (missing.Count > 0)
            throw new InvalidOperationException("Cannot read the starting spells: " + string.Join("; ", missing));

        return new StartingSpellbook(autoAttack, switchTarget, gathering, miningGather, racial, classes);
    }

    private static IReadOnlyList<uint> EveryonesGathering(IReadOnlyList<StaticObjectTypeDefinition> types) =>
        [.. types.Where(type => type.GatherSpellId != 0)
            .GroupBy(type => type.GatherSpellId)
            .Where(group => group.Count() * GatheringShareOfTypes > types.Count)
            .Select(group => group.Key)
            .Order()];

    // A lesson that names this class and not every class in play is the class's own. Each chain it
    // teaches starts with the rank the class holds by then: the first rank once the chain's lowest
    // lesson is close, and the highest rank whose lesson is at or below the starting level.
    private static IReadOnlyList<uint> ClassSpellsAt(
        IReadOnlyDictionary<uint, ItemDefinition> items, IReadOnlyDictionary<uint, SpellDefinition> spells,
        int playerClass, int level, IReadOnlySet<int> playable)
    {
        var everyClass = playable.Aggregate(0u, (mask, each) => mask | ItemClassMask.BitFor(each));
        var ownLessons = items.Values.Where(item => item.AllowableClassMask is uint mask and not 0
            && (mask & ItemClassMask.BitFor(playerClass)) != 0 && (playable.Count == 1 || (mask & everyClass) != everyClass));
        var taughtAt = SpellLearning.LevelsFor(ownLessons, spells, playerClass);

        var chains = taughtAt
            .Where(pair => spells.ContainsKey(pair.Key))
            .GroupBy(pair => spells[pair.Key].ChainFirstSpellId ?? pair.Key);
        var known = new List<(int FirstLesson, uint Spell)>();
        foreach (var chain in chains)
        {
            var firstLesson = chain.Min(pair => pair.Value);
            var held = chain.Where(pair => pair.Value <= level)
                .OrderByDescending(pair => spells[pair.Key].Rank)
                .Select(pair => (uint?)pair.Key)
                .FirstOrDefault();
            if (held is { } spell) known.Add((firstLesson, spell));
            else if (!taughtAt.ContainsKey(chain.Key) && spells.ContainsKey(chain.Key)
                && firstLesson <= level + FirstRankLead)
                known.Add((firstLesson, chain.Key));
        }

        return [.. known.OrderBy(entry => entry.FirstLesson).ThenBy(entry => entry.Spell).Select(entry => entry.Spell)];
    }

    private static int RangedWeaponsCovered(SpellDefinition autoShoot, IEnumerable<ItemDefinition> items, int playerClass) =>
        items.Count(item => item.Class == ItemClasses.Weapons && item.InventoryType == InventoryTypes.Ranged
            && item.AllowableClassMask is uint mask and not 0 && ItemClassMask.Allows(mask, playerClass)
            && item.SubClass < 32 && (autoShoot.ItemSubclassesAllowed & (1u << item.SubClass)) != 0);
}
