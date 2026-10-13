using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class TargetingTests
{
    [Fact]
    public async Task Target_AcceptsABodyTheSessionCanSee()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await TargetAsync(session, "Creature:2"));

        Assert.Contains("\"type\":\"target.changed\"", response);
        Assert.Contains("\"networkId\":\"Creature:2\"", response);
    }

    [Fact]
    public async Task Target_RefusesABodyTheSessionCannotSee()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await TargetAsync(session, "Creature:99"));

        Assert.Contains("invalid_target", response);
    }

    [Fact]
    public async Task Target_RefusesABodyLeftBehindOnAnotherMap()
    {
        var session = await TravelSession.InWorldAsync();
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.BusConfirm, 4, new { from = 1, to = 3 })));

        var response = Assert.Single(await TargetAsync(session, "Creature:2"));

        Assert.Contains("invalid_target", response);
    }

    [Fact]
    public async Task Target_IsRefusedBeforeACharacterIsChosen()
    {
        var session = await TravelSession.LoggedInAsync();

        Assert.Contains("not_in_world", Assert.Single(await TargetAsync(session, "Creature:2")));
    }

    private static async Task<IReadOnlyList<string>> TargetAsync(PlaySession session, string networkId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.TargetChange, 3, new { networkId })));
}
