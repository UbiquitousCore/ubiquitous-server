using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Server;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

internal static class TestSessions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlaySessionHub, PartyService> PartiesByHub = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlaySessionHub, GuildService> GuildsByHub = new();

    public static PartyService PartiesOf(PlaySessionHub hub) => PartiesByHub.GetValue(hub, _ => new PartyService());

    public static GuildService GuildsOf(PlaySessionHub hub, IGuildStore store) =>
        GuildsByHub.GetValue(hub, _ => new GuildService(store));

    public const int TestPasswordIterations = 1_000;

    public const int TestPurse = 5000;

    public static GameContent WithTestPurse(GameContent content) => content with { StartingCurrency = TestPurse };

    public static TestSessionBuilder Builder() => new();

    public static PlaySession Create(
        IAccountStore accounts,
        ICharacterStore characters,
        IReadOnlyList<CreatureSpawnDefinition>? creatureSpawns = null,
        IReadOnlyList<StaticObjectSpawnDefinition>? staticObjectSpawns = null,
        GameContent? content = null,
        Func<double>? spellRollUnit = null,
        Func<double>? combatRollUnit = null,
        DuelService? duels = null,
        PvpFlagService? pvpFlags = null,
        TradeService? trades = null,
        IMailStore? mailStore = null,
        AmbientService? ambient = null,
        IAuctionStore? auctionStore = null,
        IGuildStore? guildStore = null,
        InstanceService? instances = null,
        Func<double>? wanderRollUnit = null,
        IQuestLogStore? questLogStore = null,
        SharedMapRegistry? sharedMaps = null,
        WalkableGroundSource? walkableGround = null,
        IFriendStore? friendStore = null,
        IReportStore? reportStore = null,
        PlayerBlowRelay? blows = null,
        bool developmentEntry = false,
        IInventoryStore? inventoryStore = null,
        PartyService? parties = null,
        GuildService? guilds = null,
        SharedWorld? world = null,
        TimeProvider? time = null,
        PatrolRouteLibrary? patrols = null,
        IReadOnlySet<string>? developmentUserIds = null)
    {
        var stores = new SessionStores(new AccountAuthenticator(accounts, iterations: TestPasswordIterations), characters, inventoryStore, questLogStore, mailStore, auctionStore,
            guildStore, friendStore, reportStore);
        return new PlaySession(
            stores,
            new ServerContent(content ?? WithTestPurse(GameContent.Development), creatureSpawns ?? [], staticObjectSpawns ?? [],
                walkableGround) is var built && patrols is not null ? built with { Patrols = patrols } : built,
            world ?? new SharedWorld(stores, time: time)
            {
                Maps = sharedMaps ?? new SharedMapRegistry(wanderRollUnit, time),
                Duels = duels ?? new DuelService(time: time),
                PvpFlags = pvpFlags ?? new(),
                Blows = blows ?? new(),
                Trades = trades ?? new(),
                Ambient = ambient ?? new(),
                Instances = instances ?? new InstanceService(time),
                Parties = parties ?? new(),
                Guilds = guilds ?? new GuildService(guildStore ?? new InMemoryGuildStore()),
            },
            new PlaySessionOptions(developmentEntry, spellRollUnit, combatRollUnit, developmentUserIds));
    }
}

internal sealed class TestSessionBuilder
{
    private IAccountStore _accounts = new DevelopmentAccountStore("developer", "dev-secret");
    private ICharacterStore _characters = new InMemoryCharacterStore([]);
    private IReadOnlyList<CreatureSpawnDefinition>? _creatureSpawns;
    private GameContent? _content;
    private Func<double>? _combatRoll;
    private IMailStore? _mail;
    private IAuctionStore? _auctions;
    private IGuildStore? _guilds;
    private SharedWorld? _world;
    private TimeProvider? _time;
    private bool _developmentEntry;

    public TestSessionBuilder WithAccounts(IAccountStore accounts) { _accounts = accounts; return this; }

    public TestSessionBuilder WithCharacters(ICharacterStore characters) { _characters = characters; return this; }

    public TestSessionBuilder WithCreatureSpawns(IReadOnlyList<CreatureSpawnDefinition> spawns) { _creatureSpawns = spawns; return this; }

    public TestSessionBuilder WithContent(GameContent content) { _content = content; return this; }

    public TestSessionBuilder WithCombatRoll(Func<double> roll) { _combatRoll = roll; return this; }

    public TestSessionBuilder WithMailStore(IMailStore store) { _mail = store; return this; }

    public TestSessionBuilder WithAuctionStore(IAuctionStore store) { _auctions = store; return this; }

    public TestSessionBuilder WithGuildStore(IGuildStore store) { _guilds = store; return this; }

    public TestSessionBuilder InWorld(SharedWorld world) { _world = world; return this; }

    public TestSessionBuilder WithTime(TimeProvider time) { _time = time; return this; }

    public TestSessionBuilder WithDevelopmentEntry() { _developmentEntry = true; return this; }

    public PlaySession Build() => TestSessions.Create(
        _accounts, _characters, _creatureSpawns, content: _content, combatRollUnit: _combatRoll,
        mailStore: _mail, auctionStore: _auctions, guildStore: _guilds, developmentEntry: _developmentEntry,
        world: _world, time: _time);
}
