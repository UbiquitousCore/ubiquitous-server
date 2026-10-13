using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestMultiOfferAcceptTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1001, 13, 9001, 2f, 2f, 4f, 0f),
    ];

    private static readonly GameContent Content = GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(6201, 9001, 12001, 12002, 1),
            new QuestDefinition(6202, 9001, 13001, 13002, 1),
        ],
    };

    [Fact]
    public async Task QuestList_OffersBothQuestsAsAChoiceMenuWhenAGiverHasTwo()
    {
        var session = await SessionAsync();

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));

        Assert.Contains("\"type\":\"quest.listUpdated\"", list);
        Assert.Contains("6201", list);
        Assert.Contains("6202", list);
    }

    [Fact]
    public async Task QuestList_AskedAboutOneOfSeveralOffersOpensThatOnesOwnDetailsNotTheSameMenuAgain()
    {
        var session = await SessionAsync();
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" })));

        var asked = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 3,
                new { networkId = "Creature:1", questId = 6202u }))));

        Assert.Contains("\"type\":\"quest.details\"", asked);
        Assert.Contains("\"giverId\":\"Creature:1\"", asked);
        Assert.Contains("6202", asked);
        Assert.DoesNotContain("\"type\":\"quest.listUpdated\"", asked);
    }

    [Fact]
    public async Task QuestAccept_AcceptsEitherQuestASecondOfferOfferedByTheSameGiver()
    {
        var session = await SessionAsync();
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 3, new { networkId = "Creature:1", questId = 6202u })));

        var accepted = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestAccept, 4,
                new { networkId = "Creature:1", questId = 6202u }))));

        Assert.Contains("\"type\":\"quest.updated\"", accepted);
        Assert.Contains("\"status\":\"underway\"", accepted);
    }

    [Fact]
    public async Task QuestList_AskingAboutAQuestTheGiverDoesNotActuallyOfferIsRefused()
    {
        var session = await SessionAsync();

        var asked = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2,
                new { networkId = "Creature:1", questId = 999999u }))));

        Assert.Contains("\"type\":\"protocol.error\"", asked);
    }

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character()]), Spawns, [], Content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static CharacterRecord Character() => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
