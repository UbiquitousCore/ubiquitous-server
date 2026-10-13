using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public static class DevelopmentFixtures
{
    public static IReadOnlyList<CreatureSpawnDefinition> CreatureSpawns { get; } =
    [
        new(1001, 13, 9001, 5, 2, 5, 0),
        new(1010, 1, 9010, 24, 3, 4, 3.8f),
        new(1011, 1, 9011, 27, 3, -30, 3.1f),
        new(1012, 1, 9012, 2, 7, 63, 0),
        new(1013, 1, 9013, 5, 7, 64, 0),
        new(3001, 14, 9400, -676, 10, -2092, 0),
        new(3002, 14, 9400, -672, 10, -2090, 0),
        new(3003, 14, 9401, -640, 2, -2300, 0),
    ];

    public static IReadOnlyList<StaticObjectSpawnDefinition> StaticObjectSpawns { get; } =
        [new(2001, 13, 9100, -4, 2, 4, 0)];

    public static DevelopmentAccountStore Accounts(ServerOptions options) =>
        new([.. options.DevelopmentLogins().Select(login =>
            new AccountRecord(login.AccountId, login.UserId, login.PassHash, IsAdmin: true))]);

    public const int GearedLevel = 60;

    // Own choice: enough to buy every mount and riding skill while testing.
    public const int Purse = 99_000 * CoinWords.Gold;

    public static IReadOnlyList<int> StrongDefenderWarriorGear { get; } = EquipmentSlots.Build(
        (EquipmentSlots.Head, 6247),
        (EquipmentSlots.Shoulder, 6248),
        (EquipmentSlots.Chest, 6249),
        (EquipmentSlots.Legs, 6250),
        (EquipmentSlots.Feet, 6251),
        (EquipmentSlots.Hands, 6252),
        (EquipmentSlots.MainHand, 6537));

    public static IReadOnlyList<int> DevoutHealerMonkGear { get; } = EquipmentSlots.Build(
        (EquipmentSlots.Head, 6342),
        (EquipmentSlots.Shoulder, 6343),
        (EquipmentSlots.Chest, 6344),
        (EquipmentSlots.Legs, 6345),
        (EquipmentSlots.Feet, 6346),
        (EquipmentSlots.Hands, 6347),
        (EquipmentSlots.MainHand, 6281));

    public static IReadOnlyList<int> MagicSpillMageGear { get; } = EquipmentSlots.Build(
        (EquipmentSlots.Head, 6330),
        (EquipmentSlots.Shoulder, 6331),
        (EquipmentSlots.Chest, 6332),
        (EquipmentSlots.Legs, 6333),
        (EquipmentSlots.Feet, 6334),
        (EquipmentSlots.Hands, 6335),
        (EquipmentSlots.MainHand, 6281));

    public static IReadOnlyList<int> UnfeelingSneakRangerGear { get; } = EquipmentSlots.Build(
        (EquipmentSlots.Head, 6348),
        (EquipmentSlots.Shoulder, 6349),
        (EquipmentSlots.Chest, 6350),
        (EquipmentSlots.Legs, 6351),
        (EquipmentSlots.Feet, 6352),
        (EquipmentSlots.Hands, 6353),
        (EquipmentSlots.MainHand, 6583));

    private static readonly IReadOnlyDictionary<uint, int> BastionTalents = new Dictionary<uint, int>
    {
        [223] = 3, [224] = 1, [310] = 1, [311] = 5, [312] = 3, [313] = 3,
        [314] = 1, [315] = 3, [316] = 1, [317] = 5, [318] = 2, [319] = 3,
        [320] = 1, [321] = 4, [322] = 2, [323] = 5, [324] = 1, [325] = 3,
        [326] = 1, [327] = 3,
    };

    private static readonly IReadOnlyDictionary<uint, int> SeraphineTalents = new Dictionary<uint, int>
    {
        [244] = 1, [245] = 3, [246] = 1, [332] = 5, [333] = 1, [334] = 5,
        [335] = 1, [336] = 3, [337] = 1, [338] = 1, [339] = 5, [340] = 3,
        [341] = 5, [342] = 1, [344] = 3, [345] = 1, [346] = 1, [347] = 3,
        [348] = 3, [349] = 2, [351] = 1, [353] = 1,
    };

    private static readonly IReadOnlyDictionary<uint, int> LioraTalents = new Dictionary<uint, int>
    {
        [244] = 1, [246] = 1, [332] = 5, [333] = 1, [334] = 5, [335] = 1,
        [336] = 3, [337] = 1, [338] = 5, [339] = 5, [340] = 5, [341] = 3,
        [342] = 1, [343] = 1, [344] = 2, [345] = 1, [346] = 1, [347] = 2,
        [348] = 3, [349] = 2, [351] = 1, [353] = 1,
    };

    private static readonly IReadOnlyDictionary<uint, int> ArlenTalents = new Dictionary<uint, int>
    {
        [200] = 3, [201] = 5, [202] = 1, [203] = 5, [204] = 1, [205] = 3,
        [206] = 1, [207] = 3, [208] = 3, [209] = 5, [210] = 1, [211] = 3,
        [213] = 3, [214] = 1, [215] = 1, [216] = 3, [217] = 5, [218] = 3,
        [219] = 1,
    };

    private static readonly IReadOnlyDictionary<uint, int> KestrelTalents = new Dictionary<uint, int>
    {
        [354] = 1, [355] = 3, [356] = 5, [357] = 3, [358] = 1, [359] = 5,
        [360] = 5, [361] = 3, [362] = 1, [363] = 1, [364] = 5, [365] = 5,
        [366] = 5, [367] = 5, [368] = 1, [369] = 1, [370] = 1,
    };

    public static InMemoryCharacterStore Characters() => new(
    [
        Geared("dev-warrior-1", 1, "Bastion", PlayerClassIds.Warrior, PlayerBodies.Male, StrongDefenderWarriorGear, BastionTalents),
        Geared("dev-mage-1", 1, "Arlen", PlayerClassIds.Mage, PlayerBodies.Female, MagicSpillMageGear, ArlenTalents),
        Geared("dev-ranger-2", 2, "Kestrel", PlayerClassIds.Ranger, PlayerBodies.Male, UnfeelingSneakRangerGear, KestrelTalents),
        Geared("dev-healer-2", 2, "Liora", PlayerClassIds.Monk, PlayerBodies.Female, DevoutHealerMonkGear, LioraTalents),
    ]);

    private static CharacterRecord Geared(
        string guid, int accountId, string name, int playerClass, int gender, IReadOnlyList<int> gear,
        IReadOnlyDictionary<uint, int> talents)
    {
        var start = StartingLoadouts.For(PlayerRaceIds.Human, playerClass)!;
        var display = PlayerBodies.DisplayInfoFor(PlayerRaceIds.Human, gender) ?? 1;
        return new CharacterRecord(
            guid, accountId, name, gender, PlayerRaceIds.Human, playerClass,
            display, 1, 1, 1, 1,
            start.MapId, start.X, start.Y, start.Z,
            GearedLevel,
            gear,
            Currency: Purse,
            TalentRanks: talents);
    }
}
