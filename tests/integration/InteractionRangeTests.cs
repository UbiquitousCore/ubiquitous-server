using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class InteractionRangeTests
{
    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, MapId: 13, X: 0, Y: 0, Z: 0, Level: 1, Equipment: []);

    private static PlaySession SessionWithNpcAt(float x, float y, float z) =>
        TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            [new CreatureSpawnDefinition(1001, 13, 9001, x, y, z, 0)], developmentEntry: true);

    private static async Task<string> GossipAt(float x, float y, float z)
    {
        var session = SessionWithNpcAt(x, y, z);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcTalk, 1, new { networkId = "Creature:1" })));
        return Assert.Single(response);
    }

    [Fact]
    public async Task AnNpcWithinReachStillAnswers()
    {
        Assert.Contains("npc.gossip", await GossipAt(3, 0, 4));
    }

    [Fact]
    public async Task AnNpcAtExactlyTheLimitIsStillReachable()
    {
        Assert.Contains("npc.gossip", await GossipAt(6, 0, 8));
    }

    [Fact]
    public async Task AnNpcPastTheLimitIsRefused()
    {
        var response = await GossipAt(6, 0, 8.1f);

        Assert.Contains("invalid_gossip", response);
        Assert.Contains("Too far away", response);
    }

    [Fact]
    public async Task HeightCountsTowardTheDistance()
    {
        Assert.Contains("npc.gossip", await GossipAt(0, 9, 0));

        var response = await GossipAt(0, 11, 0);
        Assert.Contains("Too far away", response);
    }

    [Fact]
    public async Task AnUnknownBodyKeepsTheHandlersOwnRefusal()
    {
        var session = SessionWithNpcAt(1, 0, 1);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.NpcTalk, 1, new { networkId = "Creature:404" }))));

        Assert.Contains("not visible", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Too far away", response);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(10, 0, 0, true)]
    [InlineData(0, 10, 0, true)]
    [InlineData(10.01, 0, 0, false)]
    [InlineData(6, 0, 8, true)]
    [InlineData(6, 1, 8, false)]
    public void WithinMeasuresAllThreeAxesInclusively(double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, InteractionRange.Within(
            new Ubiquitous.Common.Position(0, 0, 0), new Ubiquitous.Common.Position((float)x, (float)y, (float)z)));
    }

    [Theory]
    [InlineData(6, 1000, 8, true)]
    [InlineData(10.01, 0, 0, false)]
    public void WithinHorizontalIgnoresHeightForTravelStops(double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, InteractionRange.WithinHorizontal(
            new Ubiquitous.Common.Position(0, 0, 0), new Ubiquitous.Common.Position((float)x, (float)y, (float)z)));
    }
}
