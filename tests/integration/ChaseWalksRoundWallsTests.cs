using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class ChaseWalksRoundWallsTests
{
    private const ushort MapId = 13;

    private static WalkableGroundSource GroundWithAWall()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, -40, -12, -2, 12, 2f);
        BakedGround.AddFloor(triangles, 2, -12, 40, 12, 2f);
        return BakedGround.Source(MapId, triangles);
    }

    private static async Task<(PlaySession Session, List<string> Log)> ChaseAcross(
        WalkableGroundSource? ground, float creatureY = 2f)
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "hunter", 3, 100000, MinDamage: 1, MaxDamage: 1,
                    NoticeDistance: 15, FactionType: UnitFactionTypes.Hostile),
            },
        };
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, MapId, -6, 2, 0, 3, [])]),
            [new CreatureSpawnDefinition(1001, MapId, 9001, 6, creatureY, 0, 0)],
            content: content, combatRollUnit: () => 1.0, walkableGround: ground);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            now += TimeSpan.FromSeconds(1);
            log.AddRange(session.TickCombat(now));
        }

        return (session, log);
    }

    [Fact]
    public async Task WithNothingToAsk_AChaserWalksStraightThroughTheGapAsItAlwaysHas()
    {
        var (_, log) = await ChaseAcross(ground: null);

        Assert.Contains(log, m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1"));
    }

    [Fact]
    public async Task WithGroundThatKnowsWhatIsSolid_AChaserDoesNotStepIntoTheGap()
    {
        var (_, log) = await ChaseAcross(GroundWithAWall());

        foreach (var moved in log.Where(m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1")))
        {
            var x = XOf(moved);
            Assert.True(x is null || x >= 2 || x <= -2,
                $"it stepped into the gap, to x = {x}");
        }
    }

    [Fact]
    public async Task StandingOnNothingTheBakeKnowsAbout_AChaserStillMoves()
    {
        var elsewhere = BakedGround.Source(MapId, BakedGround.Floor(500, 500, 540, 540, 2f));

        var (_, log) = await ChaseAcross(elsewhere);

        Assert.Contains(log, m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1"));
    }

    [Fact]
    public async Task StartingAboveTheFloor_AChaserComesDownOntoIt()
    {
        var ground = BakedGround.Source(MapId, BakedGround.Floor(-40, -12, 40, 12, 2f));

        var (_, log) = await ChaseAcross(ground, creatureY: 5f);

        var heights = log
            .Where(m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1"))
            .Select(YOf)
            .Where(y => y is not null)
            .Select(y => y!.Value)
            .ToList();

        Assert.NotEmpty(heights);
        Assert.Equal(2d, heights[^1], precision: 1);
    }

    [Fact]
    public async Task WithTheQuarryStandingWhereThereIsNoGround_AChaserStillComes()
    {
        var ground = BakedGround.Source(MapId, BakedGround.Floor(2, -12, 40, 12, 2f));

        var (_, log) = await ChaseAcross(ground);

        var moves = log
            .Where(m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1"))
            .Select(XOf)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        Assert.NotEmpty(moves);
        Assert.True(moves[^1] < moves[0], $"it got no closer: {moves[0]} to {moves[^1]}");

        Assert.True(moves[^1] < 2, $"it stopped short of its quarry, at x = {moves[^1]}");
    }

    private static WalkableGroundSource GroundWithAWayRound()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, -40, -12, -2, 12, 2f);
        BakedGround.AddFloor(triangles, 2, -12, 40, 12, 2f);
        BakedGround.AddFloor(triangles, -2, 6, 2, 12, 2f);
        return BakedGround.Source(MapId, triangles);
    }

    [Fact]
    public async Task WithAWayRoundAWall_AChaserTakesIt()
    {
        var (_, log) = await ChaseAcross(GroundWithAWayRound());

        var moves = log
            .Where(m => m.Contains("\"type\":\"entity.moved\"") && m.Contains("Creature:1"))
            .Select(m => (X: XOf(m), Z: ZOf(m)))
            .Where(p => p.X is not null && p.Z is not null)
            .Select(p => (X: p.X!.Value, Z: p.Z!.Value))
            .ToList();

        Assert.NotEmpty(moves);

        foreach (var (x, z) in moves)
        {
            Assert.True(x is >= 2 or <= -2 || z is >= 6 and <= 12,
                $"it stepped into the gap, to ({x}, {z})");
        }

        Assert.True(moves[^1].X < moves[0].X, $"it got no closer: {moves[0].X} to {moves[^1].X}");
    }

    private static double? ZOf(string message)
    {
        const string key = "\"position\":{\"x\":";
        var at = message.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        var from = message.IndexOf("\"z\":", at, StringComparison.Ordinal);
        if (from < 0) return null;
        from += 4;
        var to = message.IndexOfAny([',', '}'], from);
        return to < 0 ? null : double.Parse(message[from..to], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double? YOf(string message)
    {
        const string key = "\"position\":{\"x\":";
        var at = message.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        var from = message.IndexOf("\"y\":", at, StringComparison.Ordinal);
        if (from < 0) return null;
        from += 4;
        var to = message.IndexOf(',', from);
        return to < 0 ? null : double.Parse(message[from..to], System.Globalization.CultureInfo.InvariantCulture);
    }


    private static double? XOf(string message)
    {
        const string key = "\"position\":{\"x\":";
        var at = message.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        var from = at + key.Length;
        var to = message.IndexOf(',', from);
        return to < 0 ? null : double.Parse(message[from..to], System.Globalization.CultureInfo.InvariantCulture);
    }
}
