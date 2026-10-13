using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Modules;
using Ubiquitous.Modules.Playerbots;
using Ubiquitous.Persistence;
using Ubiquitous.Server;

namespace Ubiquitous.Playerbots.Tests;

// A server put together the way Program does, with bots loaded as a module and
// the clock advanced one world tick at a time.
internal sealed class BotTestWorld
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(400);

    private readonly WorldTick _turn;

    public BotTestWorld(GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns,
        IReadOnlyDictionary<string, string> settings, IAccountStore? realAccounts = null,
        InMemoryCharacterStore? characters = null)
        : this(new ServerContent(content, spawns, []), settings, realAccounts, characters)
    {
    }

    public BotTestWorld(ServerContent serverContent, IReadOnlyDictionary<string, string> settings,
        IAccountStore? realAccounts = null, InMemoryCharacterStore? characters = null)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Accounts = new ModuleAccounts(new AccountAuthenticator(realAccounts ?? new DevelopmentAccountStore([]), iterations: 1_000));
        Characters = characters ?? new InMemoryCharacterStore([]);
        var stores = new SessionStores(Accounts, Characters);
        var world = new SharedWorld(stores, time: Time);
        Sessions = new PlaySessionFactory(stores, serverContent, world);
        Hub = new PlaySessionHub();
        Modules = new ServerModules(NullLogger.Instance);
        Playerbots = new PlayerbotsModule();
        Modules.Register([Playerbots], module => new ModuleHost(module.Name, new ModuleSettings(settings), Modules,
            Sessions, Hub, Accounts, messagesPerSecond: 30, NullLogger.Instance));
        _turn = new WorldTick(world, Hub, Modules.TickAsync);
    }

    public FakeTimeProvider Time { get; }
    public ModuleAccounts Accounts { get; }
    public InMemoryCharacterStore Characters { get; }
    public PlaySessionFactory Sessions { get; }
    public PlaySessionHub Hub { get; }
    public ServerModules Modules { get; }
    public PlayerbotsModule Playerbots { get; }

    public IReadOnlyList<Bot> Bots => Playerbots.Manager?.Bots ?? [];

    public async Task RunUntilAsync(Func<bool> done, int mostTicks)
    {
        for (var tick = 0; tick < mostTicks && !done(); tick++)
        {
            await _turn.RunAsync(Time.GetUtcNow(), CancellationToken.None);
            Time.Advance(Tick);
        }
    }
}
