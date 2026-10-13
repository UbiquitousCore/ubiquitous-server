using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Modules.Playerbots;

namespace Ubiquitous.Playerbots.Tests;

public sealed class PlayerbotWorldTests
{
    private const ushort StartMap = 1;
    private const uint Giver = 7001;
    private const uint Rat = 7002;
    private const uint KillRat = 6001;

    internal static GameContent QuestContent() => GameContent.Development with
    {
        PlayableRaces = new HashSet<int> { PlayerRaceIds.Human },
        Npcs = new Dictionary<uint, NpcDefinition> { [Giver] = new(Giver, NpcFlags.Gossip, OffersQuests: true) },
        Creatures = new Dictionary<uint, CreatureDefinition>
        {
            [Giver] = new(Giver, "giver", 5, 100, FactionType: UnitFactionTypes.Townsfolk),
            [Rat] = new(Rat, "rat", 1, 5, MinDamage: 1, MaxDamage: 1, FactionType: UnitFactionTypes.Hostile),
        },
        Quests =
        [
            new QuestDefinition(KillRat, Giver, 1, 2, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: Rat)]),
        ],
    };

    // The human start point is (2, 7, 55) on map 1.
    internal static readonly CreatureSpawnDefinition[] QuestSpawns =
    [
        new(1, StartMap, Giver, 8f, 7f, 55f, 0f),
        new(2, StartMap, Rat, 2f, 7f, 70f, 0f),
    ];

    internal static Dictionary<string, string> OneBot() => new()
    {
        ["enabled"] = "true",
        ["randomBotCount"] = "1",
        ["seed"] = "7",
        ["maxLevel"] = "1",
        ["logInAndOut"] = "false",
    };

    [Fact]
    public async Task ARandomBot_SignsInMakesItsOwnCharacterAndEntersTheWorld()
    {
        var world = new BotTestWorld(QuestContent(), QuestSpawns, OneBot());

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);

        var bot = Assert.Single(world.Bots);
        Assert.True(bot.Playing);
        var character = Assert.IsType<Ubiquitous.Persistence.CharacterRecord>(await world.Characters.FindByNameAsync(bot.Name!));
        Assert.True(character.AccountId < 0);
        Assert.Equal(StartMap, bot.View.MapId);
    }

    [Fact]
    public async Task ARandomBot_TakesAKillQuestKillsTheCreatureAndHandsItIn()
    {
        var world = new BotTestWorld(QuestContent(), QuestSpawns, OneBot());
        var tookIt = false;
        var handedIn = false;

        await world.RunUntilAsync(() =>
        {
            if (world.Bots is not [{ Playing: true } bot]) return false;
            tookIt |= bot.View.Quests.ContainsKey(KillRat);
            handedIn = tookIt && !bot.View.Quests.ContainsKey(KillRat);
            return handedIn;
        }, mostTicks: 1500);

        Assert.True(tookIt, "the bot never accepted the quest");
        Assert.True(handedIn, "the bot never handed the quest in");
        Assert.Contains(world.Bots[0].View.KilledByMe, id => id.StartsWith("Creature:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABot_PlaysTheSameCharacterAfterARestart()
    {
        var first = new BotTestWorld(QuestContent(), QuestSpawns, OneBot());
        await first.RunUntilAsync(() => first.Bots is [{ Playing: true }], mostTicks: 50);
        var name = first.Bots[0].Name;
        await first.Modules.ShutdownAsync(CancellationToken.None);

        var second = new BotTestWorld(QuestContent(), QuestSpawns, OneBot(), characters: first.Characters);
        await second.RunUntilAsync(() => second.Bots is [{ Playing: true }], mostTicks: 50);

        Assert.Equal(name, second.Bots[0].Name);
    }

    [Fact]
    public async Task Playerbots_StayOffUnlessEnabled()
    {
        var world = new BotTestWorld(QuestContent(), QuestSpawns, new Dictionary<string, string>());

        await world.RunUntilAsync(() => false, mostTicks: 10);

        Assert.Null(world.Playerbots.Manager);
        Assert.Empty(await world.Characters.ListForAccountAsync(
            Ubiquitous.Server.ModuleAccounts.AccountIdFor($"{Ubiquitous.Server.ModuleAccounts.UserIdPrefix}playerbots.random0")));
    }

    [Fact]
    public async Task HalfTheBots_AreHumansOrElvesAndHalfOrcsOrUndead_AndEveryOneHasALook()
    {
        var content = QuestContent() with { PlayableRaces = Ubiquitous.Data.PlayerRaceIds.All };
        var world = new BotTestWorld(content, [], new Dictionary<string, string>
        {
            ["enabled"] = "true", ["randomBotCount"] = "4", ["loginsPerTick"] = "4", ["seed"] = "3",
        });

        await world.RunUntilAsync(() => world.Bots.Count == 4 && world.Bots.All(bot => bot.Playing), mostTicks: 50);

        var made = new List<Ubiquitous.Persistence.CharacterRecord>();
        foreach (var bot in world.Bots) made.Add((await world.Characters.FindByNameAsync(bot.Name!))!);
        Assert.All(made.Where((_, index) => index % 2 == 0), c => Assert.Contains(c.Race, new[] { PlayerRaceIds.Human, PlayerRaceIds.Elf }));
        Assert.All(made.Where((_, index) => index % 2 == 1), c => Assert.Contains(c.Race, new[] { PlayerRaceIds.Orc, PlayerRaceIds.Undead }));
        Assert.All(made, c => Assert.True(c is { Skin: >= 1, Face: >= 1, HairStyle: >= 1, HairColour: >= 1 }));
    }

    [Fact]
    public async Task ABotWhoseCharacterHasNoLook_MakesANewOne()
    {
        var account = Ubiquitous.Server.ModuleAccounts.AccountIdFor($"{Ubiquitous.Server.ModuleAccounts.UserIdPrefix}playerbots.random0");
        var characters = new Ubiquitous.Persistence.InMemoryCharacterStore(
        [
            new Ubiquitous.Persistence.CharacterRecord("blank", account, "Blank", 0, PlayerRaceIds.Human, PlayerClassIds.Warrior,
                0, 0, 0, 0, 0, StartMap, 2f, 7f, 55f, 1, []),
        ]);
        var world = new BotTestWorld(QuestContent(), [], OneBot(), characters: characters);

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);

        Assert.Null(await characters.GetByGuidAsync("blank"));
        Assert.Equal(1, (await characters.FindByNameAsync(world.Bots[0].Name!))!.Skin);
    }

    [Fact]
    public async Task ARandomBotGivenALevel_StartsAtItWhereCreaturesAreThatLevel()
    {
        var content = QuestContent() with
        {
            Maps = new Dictionary<ushort, MapDefinition> { [StartMap] = new(StartMap, "start"), [4] = new(4, "fields") },
            Creatures = new Dictionary<uint, CreatureDefinition>(QuestContent().Creatures)
            {
                [7004] = new(7004, "boar", 6, 60, FactionType: UnitFactionTypes.Hostile),
            },
            Stops = new Dictionary<uint, TeleportStopDefinition>
            {
                [1] = new(1, StartMap, 30f, 7f, 55f),
                [2] = new(2, 4, 100f, 3f, 100f),
            },
        };
        CreatureSpawnDefinition[] spawns = [.. QuestSpawns, new(3, 4, 7004, 110f, 3f, 100f, 0f)];
        var settings = new Dictionary<string, string>(OneBot()) { ["minLevel"] = "6", ["maxLevel"] = "6" };
        var world = new BotTestWorld(content, spawns, settings);

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);

        var character = (await world.Characters.FindByNameAsync(world.Bots[0].Name!))!;
        Assert.Equal((6, (ushort)4, 100f, 100f), (character.Level, character.MapId, character.X, character.Z));
        Assert.Equal(6, world.Bots[0].View.Level);
    }

    [Fact]
    public async Task ARandomBot_WearsALookItsBodyShips()
    {
        var content = QuestContent() with
        {
            PlayerLooks =
            [
                new PlayerLookDefinition(PlayerRaceIds.Human, PlayerBodies.Male, [3], [6], [4], [5]),
                new PlayerLookDefinition(PlayerRaceIds.Human, PlayerBodies.Female, [3], [6], [4], [5]),
            ],
        };
        var world = new BotTestWorld(content, QuestSpawns, OneBot());

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);

        var character = (await world.Characters.FindByNameAsync(world.Bots[0].Name!))!;
        Assert.Equal((3, 4, 5, 6), (character.Skin, character.HairStyle, character.HairColour, character.Face));
    }

    [Fact]
    public async Task ARandomBot_LogsOffAfterItsStintAndComesBackAsItself()
    {
        var settings = new Dictionary<string, string>(OneBot())
        {
            ["logInAndOut"] = "true",
            ["onlineMinutesMin"] = "1", ["onlineMinutesMax"] = "1",
            ["offlineMinutesMin"] = "1", ["offlineMinutesMax"] = "1",
        };
        var world = new BotTestWorld(QuestContent(), [], settings);
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var name = world.Bots[0].Name;

        await world.RunUntilAsync(() => world.Bots.Count == 0, mostTicks: 200);
        Assert.Empty(world.Bots);

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 250);
        Assert.Equal(name, Assert.Single(world.Bots).Name);
    }
}
