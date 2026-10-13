using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class TeleportTests
{
    [Fact]
    public async Task TeleportRequest_AnswersYesWithoutMovingThePlayer()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await RequestAsync(session, "Creature:1"));

        Assert.Contains("\"type\":\"npc.teleportAnswer\"", response);
        Assert.Contains("\"ok\":true", response);
        Assert.Equal((ushort)13, session.CurrentMapId);
    }

    [Fact]
    public async Task TeleportRequest_RefusesAnNpcThatSellsNoTravel()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await RequestAsync(session, "Creature:2"));

        Assert.Contains("\"ok\":false", response);
        Assert.Contains("\"networkId\":\"Creature:2\"", response);
    }

    [Fact]
    public async Task TeleportRequest_RefusesABodyThePlayerCannotSee()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await RequestAsync(session, "Creature:99"));

        Assert.Contains("\"ok\":false", response);
        Assert.Contains("\"networkId\":\"Creature:99\"", response);
    }

    [Fact]
    public async Task TeleportRequest_ReportsAProtocolErrorWhenNoBodyIsNamed()
    {
        var session = await TravelSession.InWorldAsync();

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.NpcTeleportAsk, 4, new { })));

        Assert.Contains("invalid_teleport", Assert.Single(responses));
    }

    [Fact]
    public async Task TeleportRequest_IsRefusedBeforeACharacterIsChosen()
    {
        var session = await TravelSession.LoggedInAsync();

        var responses = await RequestAsync(session, "Creature:1");

        Assert.Contains("not_in_world", Assert.Single(responses));
    }

    private static async Task<IReadOnlyList<string>> RequestAsync(PlaySession session, string networkId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcTeleportAsk, 3, new { networkId })));
}
