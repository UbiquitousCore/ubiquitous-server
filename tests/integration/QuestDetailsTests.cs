using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestDetailsTests
{
    [Fact]
    public async Task QuestList_OpensTheOnlyQuestWithItsRewards()
    {
        var session = await SessionAsync();

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));

        Assert.Contains("\"type\":\"quest.details\"", response);
        Assert.Contains("\"giverId\":\"Creature:1\"", response);
        Assert.Contains("\"rewardMoney\":250", response);
        Assert.Contains("\"rewards\":[{\"entry\":101", response);
        Assert.Contains("\"rewardChoices\":[{\"entry\":102", response);
        Assert.DoesNotContain("\"objectives\"", response);
    }

    [Fact]
    public async Task QuestList_IsStillRefusedForABodyThatIsNotAQuestGiver()
    {
        var session = await SessionAsync();

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2, new { networkId = "Creature:9" }))));

        Assert.Contains("invalid_quest", response);
    }

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 5f, 2f, 5f, 0f)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
