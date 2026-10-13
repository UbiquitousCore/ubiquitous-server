namespace Ubiquitous.Data;

public static class PlayerBodies
{
    public const int Male = 0;
    public const int Female = 1;

    private static readonly IReadOnlyDictionary<(int Race, int Gender), int> Displays =
        new Dictionary<(int, int), int>
        {
            [(PlayerRaceIds.Human, Male)] = 1684,
            [(PlayerRaceIds.Human, Female)] = 1685,
            [(PlayerRaceIds.Orc, Male)] = 1686,
            [(PlayerRaceIds.Orc, Female)] = 1687,
            [(PlayerRaceIds.Elf, Male)] = 1688,
            [(PlayerRaceIds.Elf, Female)] = 1689,
            [(PlayerRaceIds.Undead, Male)] = 1690,
            [(PlayerRaceIds.Undead, Female)] = 1691,
            [(PlayerRaceIds.Mendel, Male)] = 2566,
            [(PlayerRaceIds.Mendel, Female)] = 2619,
        };

    public static IEnumerable<(int Race, int Gender, int DisplayInfoId)> All =>
        Displays.Select(pair => (pair.Key.Race, pair.Key.Gender, pair.Value));

    public static int? DisplayInfoFor(int race, int gender) =>
        Displays.TryGetValue((race, gender), out var display) ? display : null;
}
