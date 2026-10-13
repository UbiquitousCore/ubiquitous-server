using System.Text.Json;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public sealed class NavigationViewTests
{
    private const ushort MapId = 13;

    private static WalkableGroundSource Ground()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, -10, -10, 10, 10, 2f);
        BakedGround.AddFloor(triangles, 150, 150, 160, 160, 9f);
        return BakedGround.Source(MapId, triangles);
    }

    private static CharacterRecord Character(float x = 2, float y = 2, float z = 2) => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8,
        MapId, x, y, z, 1, [100, 200]);

    private static async Task<PlaySession> EnteredSession(bool isAdmin, float x = 2, float y = 2, float z = 2)
    {
        var accounts = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: isAdmin);
        var characters = new InMemoryCharacterStore([Character(x, y, z)]);
        var session = TestSessions.Create(accounts, characters, walkableGround: Ground());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static async Task<JsonElement> Ask(PlaySession session, object payload)
    {
        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.NavigationRequest, 2, payload)));
        var reply = Assert.Single(replies);
        using var document = JsonDocument.Parse(reply);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Operator_IsSentThePolygonsAroundThem()
    {
        var session = await EnteredSession(isAdmin: true);

        var reply = await Ask(session, new { });

        Assert.Equal(WebMessageTypes.NavigationGround, reply.GetProperty("type").GetString());
        var ground = reply.GetProperty("payload");
        Assert.True(ground.GetProperty("baked").GetBoolean());
        Assert.Equal(MapId, ground.GetProperty("mapId").GetInt32());
        Assert.False(ground.GetProperty("truncated").GetBoolean());

        var polygons = ground.GetProperty("polygons");
        Assert.NotEmpty(polygons.EnumerateArray());
        Assert.True(polygons.GetArrayLength() < ground.GetProperty("polygonTotal").GetInt32());
        foreach (var polygon in polygons.EnumerateArray())
        {
            var vertices = polygon.GetProperty("vertices").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.True(vertices.Length >= 9 && vertices.Length % 3 == 0, "corners travel as x/y/z triples");
            for (int i = 0; i < vertices.Length; i += 3)
            {
                Assert.InRange(vertices[i], -60f, 60f);
            }
        }

        Assert.Contains(polygons.EnumerateArray(), p => p.GetProperty("links").GetArrayLength() > 0);

        Assert.Equal(
            NavigationBaker.CellSize,
            ground.GetProperty("parameters").GetProperty("cellSize").GetSingle(),
            precision: 4);
    }

    [Fact]
    public async Task Operator_IsToldWhatTheyAreStandingOnAndWhatHeightItClaims()
    {
        var session = await EnteredSession(isAdmin: true);

        var ground = (await Ask(session, new { })).GetProperty("payload");

        Assert.True(ground.GetProperty("standingOn").GetInt32() >= 0);
        BakedGround.AssertGround(2f, ground.GetProperty("groundY").GetSingle());
        Assert.Equal(2f, ground.GetProperty("playerY").GetSingle(), precision: 4);
    }

    [Fact]
    public async Task OperatorStandingWellAboveTheBakedFloor_SeesTheGapBetweenTheTwoHeights()
    {
        var session = await EnteredSession(isAdmin: true, y: 40);

        var ground = (await Ask(session, new { })).GetProperty("payload");

        Assert.False(ground.TryGetProperty("standingOn", out _));
        Assert.False(ground.TryGetProperty("groundY", out _));
        Assert.Equal(40f, ground.GetProperty("playerY").GetSingle(), precision: 4);

        BakedGround.AssertGround(2f, ground.GetProperty("nearestY").GetSingle());
    }

    [Fact]
    public async Task OperatorOffTheBakedGroundEntirely_IsToldSoWithoutTheAnswerFailing()
    {
        var session = await EnteredSession(isAdmin: true, x: -200, z: -200);

        var ground = (await Ask(session, new { })).GetProperty("payload");

        Assert.True(ground.GetProperty("baked").GetBoolean());
        Assert.False(ground.TryGetProperty("standingOn", out _));
        Assert.False(ground.TryGetProperty("groundY", out _));
        Assert.Empty(ground.GetProperty("polygons").EnumerateArray());
    }

    [Fact]
    public async Task Radius_DecidesHowMuchGroundComesBackAndIsCapped()
    {
        var session = await EnteredSession(isAdmin: true);

        var near = (await Ask(session, new { radius = 1.5f })).GetProperty("payload");
        int close = near.GetProperty("polygons").GetArrayLength();
        Assert.InRange(close, 1, near.GetProperty("polygonTotal").GetInt32() - 1);

        var far = (await Ask(session, new { radius = 100_000f })).GetProperty("payload");
        Assert.Equal(240f, far.GetProperty("radius").GetSingle(), precision: 4);
        Assert.True(far.GetProperty("polygons").GetArrayLength() > close);
    }

    [Fact]
    public async Task MapWithNoBake_AnswersNothingRatherThanFailing()
    {
        var accounts = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true);
        var characters = new InMemoryCharacterStore([Character()]);
        var session = TestSessions.Create(accounts, characters,
            walkableGround: new WalkableGroundSource(new Dictionary<ushort, NavigationMesh>()));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var reply = await Ask(session, new { });

        Assert.Equal(WebMessageTypes.NavigationGround, reply.GetProperty("type").GetString());
        var ground = reply.GetProperty("payload");
        Assert.False(ground.GetProperty("baked").GetBoolean());
        Assert.Empty(ground.GetProperty("polygons").EnumerateArray());
        Assert.False(ground.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task ServerHoldingNoWalkableGroundAtAll_AnswersNothingRatherThanFailing()
    {
        var accounts = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true);
        var characters = new InMemoryCharacterStore([Character()]);
        var session = TestSessions.Create(accounts, characters);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var ground = (await Ask(session, new { })).GetProperty("payload");

        Assert.False(ground.GetProperty("baked").GetBoolean());
    }

    [Fact]
    public async Task OrdinaryAccount_IsRefused()
    {
        var session = await EnteredSession(isAdmin: false);

        var reply = await Ask(session, new { });

        Assert.Equal(WebMessageTypes.ProtocolError, reply.GetProperty("type").GetString());
        Assert.Equal("forbidden", reply.GetProperty("payload").GetProperty("code").GetString());
    }

    [Fact]
    public async Task BeforeACharacterIsChosen_TheAnswerIsARefusalRatherThanAnEmptyMesh()
    {
        var accounts = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true);
        var characters = new InMemoryCharacterStore([Character()]);
        var session = TestSessions.Create(accounts, characters, walkableGround: Ground());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var reply = await Ask(session, new { });

        Assert.Equal(WebMessageTypes.ProtocolError, reply.GetProperty("type").GetString());
        Assert.Equal("not_in_world", reply.GetProperty("payload").GetProperty("code").GetString());
    }
}
