using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Game;
using Ubiquitous.Modules;
using Ubiquitous.Modules.Playerbots;
using Ubiquitous.Protocol.Web;
using Position = Ubiquitous.Common.Position;
using WirePosition = Ubiquitous.Protocol.Web.Position;

namespace Ubiquitous.Playerbots.Tests;

public sealed class BotWalkingTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class Connection : IClientConnection
    {
        public List<string> Sent { get; } = [];
        public List<string> Inbox { get; } = [];
        public bool IsOpen => true;

        public Task SendAsync(string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> TakeReceived()
        {
            var taken = Inbox.ToList();
            Inbox.Clear();
            return taken;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // A map whose only way from the start to (10, 0, 10) turns at (10, 0, 0).
    private sealed class CornerMap : IModuleHost
    {
        public bool HasRoute { get; init; } = true;
        public bool OpenField { get; init; }
        public bool FlipRoutes { get; init; }
        private int _routes;
        public bool BotOnGround { get; init; } = true;
        public float FloorY { get; init; }
        public float PocketArea { get; init; }
        public bool PocketStrip { get; init; }
        public bool HasWalkableGround(ushort mapId) => true;
        public float? Water { get; init; }
        public float? WaterSurface(ushort mapId, float x, float z) => x > 3 ? Water : null;

        // Everything is open ground, except where a pocket or no ground at all is asked for.
        public float WalkableAreaUnder(ushort mapId, Position at) =>
            !BotOnGround && at.X == 0 && at.Z == 0 ? 0f
            : Math.Abs(at.Y - FloorY) > 0.6f ? 0f
            : PocketStrip && at.Z is > 3 and < 6 ? 500f
            : PocketArea > 0 && at.Z > 5 ? PocketArea
            : 1_000_000f;

        public ILogger Logger => NullLogger.Instance;
        public ModuleSettings Settings { get; } = new();
        public GameContent Content => PlayerbotWorldTests.QuestContent();
        public IReadOnlyList<Ubiquitous.Data.CreatureSpawnDefinition> CreatureSpawns => [];
        public TimeProvider Time => TimeProvider.System;
        public float? GroundHeight(ushort mapId, float x, float z, float nearY) => FloorY;

        public IReadOnlyList<Position>? FindRoute(ushort mapId, Position from, Position to) =>
            !HasRoute ? null
            : OpenField ? [to]
            : FlipRoutes && _routes++ % 2 == 1 ? [new Position(-10, FloorY, 0), to]
            : [new Position(10, FloorY, 0), to];

        public Task<ModuleLogin> ReserveLoginAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModuleLogin(key, "secret"));

        public Task<bool> StartAtLevelAsync(ModuleLogin login, string characterName, int level,
            (ushort MapId, Position At)? start = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public IClientConnection Connect() => throw new NotSupportedException();
        public bool IsPlayerNear(ushort mapId, Position at, float within) => false;
        public void OnWorldTick(Func<DateTimeOffset, CancellationToken, Task> tick) { }
        public void OnShutdown(Func<CancellationToken, Task> shutdown) { }
    }

    private static string Message(string type, object payload) =>
        WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload));

    // Signs a bot in beside a quest giver at (10, 0, 10) and returns what it sends over three turns.
    private static async Task<List<string>> WalkTowardsAGiverAsync(CornerMap map, float giverZ = 10, int seed = 1, int turns = 3)
    {
        var connection = new Connection();
        var settings = PlayerbotSettings.From(new ModuleSettings());
        var bot = new Bot(map, connection, new ModuleLogin("bot", "secret"),
            new BotBrain(new BotKnowledge(map.Content), settings, new Random(seed), _ => false), new Random(seed),
            new HashSet<int> { Ubiquitous.Data.PlayerRaceIds.Human });

        connection.Inbox.Add(Message(WebMessageTypes.AccountLoginResult, new ResultPayload(true, 2)));
        await bot.TickAsync(Start, CancellationToken.None);
        await bot.TickAsync(Start, CancellationToken.None);
        connection.Inbox.Add(Message(WebMessageTypes.CharactersListed, new CharactersListedPayload(
            [new CharacterSummary("g", "Walker", 1, 0, 1, 1, 1, new Appearance(1, 1, 1, 1), 1, new WirePosition(0, 0, 0), [])])));
        await bot.TickAsync(Start, CancellationToken.None);
        connection.Inbox.Add(Message(WebMessageTypes.WorldEntered, new WorldEnteredPayload(1, new WirePosition(0, 0, 0),
            WebNetworkId.FromString("player:g"))));
        connection.Inbox.Add(Message(WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(
            WebNetworkId.FromString("Creature:1"), 7001, new WirePosition(10, 0, giverZ), 0, OffersQuests: true, Faction: 1)));
        connection.Inbox.Add(Message(WebMessageTypes.QuestStatuses, new QuestStatusesPayload(
            [new(WebNetworkId.FromString("Creature:1"), QuestStates.Offered)])));
        await bot.TickAsync(Start, CancellationToken.None);
        connection.Sent.Clear();

        for (var turn = 1; turn <= turns; turn++)
            await bot.TickAsync(Start.AddMilliseconds(400 * turn), CancellationToken.None);
        return connection.Sent;
    }

    private static List<PlayerMovePayload> Moves(IEnumerable<string> sent) =>
        [.. sent.Where(message => message.Contains(WebMessageTypes.PlayerMove, StringComparison.Ordinal))
            .Select(message => WebMessageJson.DeserializePayload<PlayerMovePayload>(WebMessageJson.Deserialize(message).Payload))];

    [Fact]
    public async Task ABot_WalksTheMapsRouteRatherThanStraightAtItsGoal()
    {
        var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap()));

        Assert.NotEmpty(moves);
        Assert.All(moves, move => Assert.Equal(0d, move.Z, 3));
        Assert.True(moves[^1].X > moves[0].X);
    }

    [Fact]
    public async Task AGoalWithNoWalkableRoute_IsNeverWalkedStraightAt()
    {
        var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap { HasRoute = false }));

        Assert.DoesNotContain(moves, move => move.Moving && move.Z > 0.01);
    }

    [Fact]
    public async Task ABotInsideAWall_AsksToBeUnstuckInsteadOfWalking()
    {
        var sent = await WalkTowardsAGiverAsync(new CornerMap { BotOnGround = false });

        Assert.Single(sent, message => message.Contains(WebMessageTypes.PlayerUnstuck, StringComparison.Ordinal));
        Assert.Empty(Moves(sent));
    }

    [Fact]
    public async Task BotsSentTheSameLongWay_DoNotAllTakeTheSameLine()
    {
        var sideways = new List<double>();
        for (var seed = 1; seed <= 12; seed++)
        {
            var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap { OpenField = true }, giverZ: 80, seed));
            sideways.Add(moves.Max(move => Math.Abs(move.X - move.Z * 10 / 80)));
        }

        Assert.Contains(sideways, off => off < 0.01);
        Assert.Contains(sideways, off => off > 0.5);
    }

    [Fact]
    public async Task ABotJustBelowAFloor_StepsUpOntoIt()
    {
        var sent = await WalkTowardsAGiverAsync(new CornerMap { FloorY = 1.3f });

        Assert.DoesNotContain(sent, message => message.Contains(WebMessageTypes.PlayerUnstuck, StringComparison.Ordinal));
        Assert.All(Moves(sent), move => Assert.Equal(1.3, move.Y, 3));
        Assert.Contains(Moves(sent), move => move.Moving);
    }

    [Fact]
    public async Task AGiverInAPocket_IsApproachedFromTheMainGroundButNeverEntered()
    {
        var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap { PocketArea = 500, OpenField = true }, turns: 10));

        Assert.Contains(moves, move => move.Moving);
        Assert.All(moves, move => Assert.True(move.Z <= 5));
    }

    [Fact]
    public async Task AWalkKeepsItsRouteWhileTheGoalStandsStill()
    {
        var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap { FlipRoutes = true }, giverZ: 40, turns: 15));

        Assert.True(moves.Count >= 2);
        Assert.All(moves.Zip(moves.Skip(1)), pair => Assert.True(pair.Second.X >= pair.First.X));
    }

    [Fact]
    public async Task ABotThatSlipsIntoAPocket_StepsBackRatherThanAskingToBeUnstuck()
    {
        var sent = await WalkTowardsAGiverAsync(new CornerMap { OpenField = true, PocketStrip = true }, turns: 8);

        var moves = Moves(sent);
        Assert.DoesNotContain(sent, message => message.Contains(WebMessageTypes.PlayerUnstuck, StringComparison.Ordinal));
        var slipped = moves.FindIndex(move => move.Z > 3);
        Assert.True(slipped >= 0);
        Assert.True(moves[slipped + 1] is { Moving: false, Z: <= 3 });
    }

    [Fact]
    public async Task ABotKeepsWalkingWhileItRefreshesQuestMarks()
    {
        var sent = await WalkTowardsAGiverAsync(new CornerMap { OpenField = true }, giverZ: 80, turns: 20);

        Assert.Contains(sent, message => message.Contains(WebMessageTypes.QuestRefreshStatuses, StringComparison.Ordinal));
        Assert.All(Moves(sent), move => Assert.True(move.Moving));
    }

    [Fact]
    public async Task InDeepWater_ABotSwimsAtTheSwimLineAndMoreSlowly()
    {
        var moves = Moves(await WalkTowardsAGiverAsync(new CornerMap { OpenField = true, Water = 3f }, giverZ: 40, turns: 8));

        var swims = moves.Where(move => move.X > 3.01).ToList();
        Assert.NotEmpty(swims);
        Assert.All(swims, move => Assert.Equal(MovementState.Swimming, move.State));
        Assert.All(swims, move => Assert.Equal(3 - Ubiquitous.Data.NavigationAreas.SwimDepth, move.Y, 3));
        var strokes = swims.Zip(swims.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2))).ToList();
        Assert.All(strokes, stroke => Assert.True(stroke <= Bot.SwimSpeed * 0.4 + 0.01));
        Assert.All(moves.Where(move => move.X < 2.9), move => Assert.NotEqual(MovementState.Swimming, move.State));
    }
}
