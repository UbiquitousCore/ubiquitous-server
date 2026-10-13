using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class StartingLoadouts
{
    private const int MinorHealingPotion = 1168;
    private const int DinnerBread = 3000;
    private const int MinorManaPotion = 3008;

    private static IReadOnlyList<StartingLoadoutItem> Consumables =>
    [
        new(MinorHealingPotion, 17, 5),
        new(DinnerBread, 18, 20),
        new(MinorManaPotion, 19, 5),
    ];

    private static IReadOnlyList<StartingLoadoutItem> WarriorKit =>
    [
        new(3070, EquipmentSlots.Chest, 0),
        new(3007, EquipmentSlots.Legs, 0),
        new(3071, EquipmentSlots.Feet, 0),
        new(1103, EquipmentSlots.MainHand, 0),
        new(1213, EquipmentSlots.OffHand, 0),
        .. Consumables,
    ];

    private static IReadOnlyList<StartingLoadoutItem> MageKit =>
    [
        new(1109, EquipmentSlots.Chest, 0),
        new(1234, EquipmentSlots.Legs, 0),
        new(3068, EquipmentSlots.Feet, 0),
        new(5007, EquipmentSlots.MainHand, 0),
        .. Consumables,
    ];

    private static IReadOnlyList<StartingLoadoutItem> RangerKit =>
    [
        new(1053, EquipmentSlots.Chest, 0),
        new(1060, EquipmentSlots.Legs, 0),
        new(1063, EquipmentSlots.Feet, 0),
        new(3907, EquipmentSlots.MainHand, 0),
        new(3907, EquipmentSlots.OffHand, 0),
        new(4149, EquipmentSlots.Ranged, 0),
        .. Consumables,
    ];

    private static IReadOnlyList<StartingLoadoutItem> MonkKit =>
    [
        new(4966, EquipmentSlots.Chest, 0),
        new(1104, EquipmentSlots.Legs, 0),
        new(1064, EquipmentSlots.Feet, 0),
        new(4099, EquipmentSlots.MainHand, 0),
        .. Consumables,
    ];

    private static IReadOnlyList<uint> WarriorSpells =>
        [5233, 5253, 5350, 23, 57, 58, 108, 1822, 111];

    private static IReadOnlyList<uint> MageSpells =>
        [5000, 5121, 5118, 23, 57, 108, 1822, 111, 89];

    private static IReadOnlyList<uint> RangerSpells =>
        [5692, 5815, 5813, 23, 57, 58, 108, 648, 690, 1822, 111];

    private static IReadOnlyList<uint> MonkSpells =>
        [5460, 5470, 5574, 23, 57, 108, 1822, 111, 89];

    private static IReadOnlyList<uint> RacialSpells(int race) => race switch
    {
        PlayerRaceIds.Human => [6995],
        PlayerRaceIds.Elf => [6996],
        PlayerRaceIds.Orc => [6997],
        PlayerRaceIds.Undead => [6998],
        _ => [],
    };

    public static void ValidateAgainst(IReadOnlyDictionary<uint, SpellDefinition> spells) =>
        Validate(All, spells);

    public static void Validate(
        IEnumerable<CharacterStartingLoadout> loadouts, IReadOnlyDictionary<uint, SpellDefinition> spells)
    {
        var unknown = new List<string>();
        foreach (var loadout in loadouts)
        {
            foreach (var spellId in loadout.Spells)
            {
                if (spells.ContainsKey(spellId)) continue;
                unknown.Add($"race {loadout.Race}/class {loadout.PlayerClass} names unknown spell {spellId}");
            }
        }

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                "Starting loadouts name spells with no definition: "
                + string.Join("; ", unknown.Distinct()));
        }
    }

    private static (ushort MapId, float X, float Y, float Z, float Orientation) StartPoint(int race) => race switch
    {
        PlayerRaceIds.Orc or PlayerRaceIds.Undead => (7, -2640f, 33f, 100f, 0f),
        PlayerRaceIds.Mendel => (6, -860f, -6.5f, 20f, 0f),
        _ => (1, 2f, 7f, 55f, 0f),
    };

    private const uint MeleeButtonSpell = 108;

    private static IReadOnlyList<StartingActionButton> ActionBar(int playerClass) =>
        [new StartingActionButton(0, MeleeButtonSpell), new StartingActionButton(1, Opener(playerClass))];

    private static uint Opener(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => 5233,
        PlayerClassIds.Mage => 5000,
        PlayerClassIds.Ranger => 5692,
        _ => 5460,
    };

    private static IReadOnlyList<StartingLoadoutItem> Kit(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => WarriorKit,
        PlayerClassIds.Mage => MageKit,
        PlayerClassIds.Ranger => RangerKit,
        _ => MonkKit,
    };

    private static IReadOnlyList<uint> ClassSpells(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => WarriorSpells,
        PlayerClassIds.Mage => MageSpells,
        PlayerClassIds.Ranger => RangerSpells,
        _ => MonkSpells,
    };

    private static CharacterStartingLoadout Loadout(
        int race, int playerClass, int strength, int agility, int stamina, int intellect)
    {
        var (mapId, x, y, z, orientation) = StartPoint(race);
        return new CharacterStartingLoadout(
            race, playerClass, mapId, x, y, z, orientation,
            new StartingAttributes(strength, agility, stamina, intellect),
            Kit(playerClass),
            [.. ClassSpells(playerClass), .. RacialSpells(race)],
            ActionBar(playerClass));
    }

    public static IReadOnlyList<CharacterStartingLoadout> All { get; } =
    [
        Loadout(PlayerRaceIds.Human,  PlayerClassIds.Warrior, 21, 12, 18, 9),
        Loadout(PlayerRaceIds.Human,  PlayerClassIds.Mage,    10, 10, 15, 25),
        Loadout(PlayerRaceIds.Human,  PlayerClassIds.Ranger,  13, 22, 18, 10),
        Loadout(PlayerRaceIds.Human,  PlayerClassIds.Monk,  14, 11, 20, 20),
        Loadout(PlayerRaceIds.Elf,    PlayerClassIds.Warrior, 18, 15, 16, 11),
        Loadout(PlayerRaceIds.Elf,    PlayerClassIds.Mage,     7, 13, 13, 27),
        Loadout(PlayerRaceIds.Elf,    PlayerClassIds.Ranger,  10, 25, 16, 12),
        Loadout(PlayerRaceIds.Elf,    PlayerClassIds.Monk,  11, 14, 18, 22),
        Loadout(PlayerRaceIds.Orc,    PlayerClassIds.Warrior, 23, 11, 18, 8),
        Loadout(PlayerRaceIds.Orc,    PlayerClassIds.Mage,    12,  9, 15, 24),
        Loadout(PlayerRaceIds.Orc,    PlayerClassIds.Ranger,  15, 21, 18, 9),
        Loadout(PlayerRaceIds.Orc,    PlayerClassIds.Monk,  16, 10, 20, 19),
        Loadout(PlayerRaceIds.Undead, PlayerClassIds.Warrior, 19, 12, 17, 12),
        Loadout(PlayerRaceIds.Undead, PlayerClassIds.Mage,     8, 10, 14, 28),
        Loadout(PlayerRaceIds.Undead, PlayerClassIds.Ranger,  11, 22, 17, 13),
        Loadout(PlayerRaceIds.Undead, PlayerClassIds.Monk,  12, 11, 19, 23),
        Loadout(PlayerRaceIds.Mendel, PlayerClassIds.Warrior, 19, 16, 21, 9),
        Loadout(PlayerRaceIds.Mendel, PlayerClassIds.Mage,     9, 16, 16, 24),
        Loadout(PlayerRaceIds.Mendel, PlayerClassIds.Ranger,   9, 26, 16, 14),
        Loadout(PlayerRaceIds.Mendel, PlayerClassIds.Monk,  15, 17, 18, 15),
    ];

    public static CharacterStartingLoadout? For(int race, int playerClass) =>
        All.FirstOrDefault(loadout => loadout.Race == race && loadout.PlayerClass == playerClass);
}
