using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestRewardTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1001, 13, 9001, 5f, 2f, 5f, 0f),
    ];

    [Fact]
    public async Task Complete_RefusesAPlinthTheQuestDoesNotOffer()
    {
        var session = await AcceptedAsync();

        var response = await CompleteAsync(session, rewardIndex: 7);

        Assert.Contains("invalid_quest", Assert.Single(response));
        Assert.Contains(await CompleteAsync(session, rewardIndex: 0),
            message => message.Contains("quest.updated"));
    }

    [Fact]
    public async Task Complete_PaysTheChosenPlinthAndNotTheOther()
    {
        var session = await AcceptedAsync();

        var response = await CompleteAsync(session, rewardIndex: 1);

        var added = response.Where(message => message.Contains("inventory.itemAdded")).ToArray();
        Assert.Equal(2, added.Length);
        Assert.Contains(added, message => message.Contains("\"entry\":101"));
        Assert.Contains(added, message => message.Contains("\"entry\":103"));
        Assert.DoesNotContain(added, message => message.Contains("\"entry\":102"));
    }

    [Fact]
    public async Task Complete_CannotBeClaimedTwice()
    {
        var session = await AcceptedAsync();
        await CompleteAsync(session, rewardIndex: 0);

        var again = await CompleteAsync(session, rewardIndex: 0);

        Assert.Contains("invalid_quest", Assert.Single(again));
    }

    [Fact]
    public async Task Complete_RefusesATollThePlayerCannotAfford()
    {
        var content = GameContent.Development with
        {
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [5001] = new(5001, -6000, [], []),
            },
            StartingCurrency = 100,
        };
        var session = await AcceptedAsync(content);

        var response = await CompleteAsync(session, rewardIndex: 0);

        Assert.Contains("invalid_quest", Assert.Single(response));
        Assert.DoesNotContain(response, message => message.Contains("player.updated"));
    }

    [Fact]
    public async Task Complete_ChargesTheTollAndReportsTheNewBalance()
    {
        var content = GameContent.Development with
        {
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [5001] = new(5001, -60, [], []),
            },
            StartingCurrency = 100,
        };
        var session = await AcceptedAsync(content);

        var response = await CompleteAsync(session, rewardIndex: 0);

        Assert.Contains(response, message => message.Contains("\"type\":\"quest.updated\"")
            && message.Contains("complete"));
        Assert.Contains(response, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"gold\":40"));
    }

    [Fact]
    public async Task Complete_PaysTheQuestsOwnScaledXp()
    {
        var content = ContentPayingXp(rewardXp: 1000, questOwnLevel: 20);
        var session = await AcceptedAsync(content);

        var response = await CompleteAsync(session, rewardIndex: 0);

        Assert.Contains(response, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"xp\":1200"));
        Assert.Contains(response, message => message.Contains("\"type\":\"player.expGained\"")
            && message.Contains("\"amount\":1200") && message.Contains("\"levelUp\":false"));
    }

    [Fact]
    public async Task Complete_LevelsUpOffQuestXpAndSaysSo()
    {
        var content = ContentPayingXp(rewardXp: 2000, questOwnLevel: 4);
        var session = await AcceptedAsync(content);

        var response = await CompleteAsync(session, rewardIndex: 0);

        Assert.Contains(response, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"level\":5") && message.Contains("\"xp\":400"));
        Assert.Contains(response, message => message.Contains("\"type\":\"player.expGained\"")
            && message.Contains("\"levelUp\":true"));
    }

    [Fact]
    public async Task Complete_SaysNothingAboutXpForAQuestThatPaysNone()
    {
        var session = await AcceptedAsync();

        var response = await CompleteAsync(session, rewardIndex: 0);

        Assert.DoesNotContain(response, message => message.Contains("player.expGained"));
    }

    private static GameContent ContentPayingXp(int rewardXp, int questOwnLevel)
    {
        var quest = GameContent.Development.Quests.Single(candidate => candidate.Id == 5001);
        return GameContent.Development with
        {
            Quests = [.. GameContent.Development.Quests
                .Select(candidate => candidate.Id == 5001 ? quest with { Level = questOwnLevel } : candidate)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [5001] = new(5001, 0, [], [], Xp: rewardXp),
            },
        };
    }

    private static async Task<IReadOnlyList<string>> CompleteAsync(PlaySession session, int rewardIndex) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 3,
            new { networkId = "Creature:1", questId = 5001, rewardIndex })));

    private static Task<PlaySession> AcceptedAsync() => AcceptedAsync(GameContent.Development);

    private static async Task<PlaySession> AcceptedAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns, null, content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));
        return session;
    }
}
