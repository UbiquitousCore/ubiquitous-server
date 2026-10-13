using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class BusTravelTests
{
    [Fact]
    public async Task Request_QuotesAFareFromTheStopThePlayerIsStandingAt()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await RequestAsync(session, 1, 2));

        Assert.Contains("\"type\":\"bus.fare\"", response);
        Assert.Contains("\"from\":1", response);
        Assert.Contains("\"to\":2", response);
    }

    [Fact]
    public async Task Request_SaysNothingAboutAJourneyItWillNotSell()
    {
        var session = await TravelSession.InWorldAsync();

        Assert.Empty(await RequestAsync(session, 3, 1));
        Assert.Empty(await RequestAsync(session, 1, 99));
    }

    [Fact]
    public async Task Request_QuotesNothingForAJourneyToWhereYouStand()
    {
        var session = await TravelSession.InWorldAsync();

        var response = Assert.Single(await RequestAsync(session, 1, 1));

        Assert.Contains("\"cost\":0", response);
    }

    [Fact]
    public async Task Confirm_ChargesTheFareAndRunsTheEnterWorldHandshakeAgain()
    {
        var session = await TravelSession.InWorldAsync();

        var responses = await ConfirmAsync(session, 1, 3);

        Assert.Contains(responses, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"gold\":4500"));
        Assert.Contains(responses, message => message.Contains("\"type\":\"world.loading\"")
            && message.Contains("\"map\":14"));
        Assert.Contains(responses, message => message.Contains("\"type\":\"world.entered\"")
            && message.Contains("\"map\":14"));
        Assert.Equal((ushort)14, session.CurrentMapId);
    }

    [Fact]
    public async Task Confirm_TakesEverythingTheOldMapHadStandingWithIt()
    {
        var session = await TravelSession.InWorldAsync();

        var responses = await ConfirmAsync(session, 1, 3);

        Assert.Equal(3, responses.Count(message => message.Contains("\"type\":\"entity.removed\"")));
        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"entity.spawned\""));

        var attack = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 9, new { targetId = "Creature:2" })));
        Assert.Contains("invalid_target", Assert.Single(attack));
    }

    [Fact]
    public async Task Confirm_StandsThePlayerDownWhenTheFightIsLeftBehind()
    {
        var session = await TravelSession.InWorldAsync();
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 5, new { targetId = "Creature:2" })));

        var responses = await ConfirmAsync(session, 1, 3);

        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.halted\"")
            && message.Contains("\"Creature:2\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.exitCombat\""));
        Assert.Empty(session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Confirm_SaysNothingAndMovesNobodyWhenTheJourneyIsRefused()
    {
        var session = await TravelSession.InWorldAsync();

        Assert.Empty(await ConfirmAsync(session, 3, 1));
        Assert.Equal((ushort)13, session.CurrentMapId);
    }

    [Fact]
    public async Task Confirm_ExplicitlyRefusesAJourneyThePlayerCannotAfford()
    {
        var content = GameContent.Development with { StartingCurrency = 10 };
        var session = await TravelSession.InWorldAsync(content);

        var response = Assert.Single(await ConfirmAsync(session, 1, 3));

        Assert.Contains("insufficient_funds", response);
        Assert.Equal((ushort)13, session.CurrentMapId);
    }

    private static async Task<IReadOnlyList<string>> RequestAsync(PlaySession session, int from, int to) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.BusRequest, 3, new { from, to })));

    private static async Task<IReadOnlyList<string>> ConfirmAsync(PlaySession session, int from, int to) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.BusConfirm, 4, new { from, to })));
}
