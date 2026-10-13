using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestSharingTests
{
    private const uint Quest = 6400;
    private const uint HighQuest = 6500;

    private static readonly GameContent Content = GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(Quest, 9001, 13101, 13102, 1),
            new QuestDefinition(HighQuest, 9001, 13103, 13104, 30),
        ],
    };

    private static readonly CreatureSpawnDefinition[] Spawns = [new(1001, 13, 9001, 2f, 2f, 4f, 0f)];

    [Fact]
    public async Task A_MemberNearby_IsOfferedTheQuest_AndTakingItPutsItInTheirLog()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "qa1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "qb1", "Borin");
        await PartyAsync(ayla, borin, "qb1");
        await AcceptFromGiverAsync(ayla, Quest);

        var shared = await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = Quest });

        Assert.Empty(shared);
        var offer = Assert.Single(borinInbox, m => m.Contains("\"type\":\"quest.shareOffered\""));
        Assert.Contains("\"sharerId\":\"player:char-qa1\"", offer);
        Assert.Contains($"\"questId\":{Quest}", offer);
        Assert.Contains("\"titleStringId\":13101", offer);

        var taken = await SendAsync(borin, WebMessageTypes.QuestShareAnswer, 21, new { questId = Quest, accept = true });

        Assert.Contains(taken, m => m.Contains("\"type\":\"quest.updated\"") && m.Contains($"\"id\":{Quest}"));
        var told = Assert.Single(aylaInbox, m => m.Contains("\"type\":\"quest.shareResult\""));
        Assert.Contains("\"memberId\":\"player:char-qb1\"", told);
        Assert.Contains("\"result\":\"accepted\"", told);
    }

    [Fact]
    public async Task Declining_TellsTheSharer_AndLeavesNothingToAnswer()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "qa2", "Ayla");
        var (borin, _) = await JoinAsync(hub, "qb2", "Borin");
        await PartyAsync(ayla, borin, "qb2");
        await AcceptFromGiverAsync(ayla, Quest);
        await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = Quest });

        var declined = await SendAsync(borin, WebMessageTypes.QuestShareAnswer, 21, new { questId = Quest, accept = false });

        Assert.Empty(declined);
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"quest.shareResult\"") && m.Contains("\"result\":\"declined\""));
        var again = await SendAsync(borin, WebMessageTypes.QuestShareAnswer, 22, new { questId = Quest, accept = true });
        Assert.Contains(again, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task A_MemberWhoAlreadyHasIt_IsNotOffered_AndTheSharerIsToldSo()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "qa3", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "qb3", "Borin");
        await PartyAsync(ayla, borin, "qb3");
        await AcceptFromGiverAsync(ayla, Quest);
        await AcceptFromGiverAsync(borin, Quest);

        var shared = await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = Quest });

        Assert.Contains(shared, m => m.Contains("\"type\":\"quest.shareResult\"") && m.Contains("\"result\":\"alreadyHas\""));
        Assert.DoesNotContain(borinInbox, m => m.Contains("quest.shareOffered"));
    }

    [Fact]
    public async Task A_MemberTooLowForIt_IsNotEligible()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "qa4", "Ayla", level: 40);
        var (borin, _) = await JoinAsync(hub, "qb4", "Borin");
        await PartyAsync(ayla, borin, "qb4");
        await AcceptFromGiverAsync(ayla, HighQuest);

        var shared = await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = HighQuest });

        Assert.Contains(shared, m => m.Contains("\"type\":\"quest.shareResult\"") && m.Contains("\"result\":\"notEligible\""));
    }

    [Fact]
    public async Task A_MemberFarAway_IsOutOfReach()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "qa5", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "qb5", "Borin", x: 1 + (float)QuestService.ShareRange + 5);
        await PartyAsync(ayla, borin, "qb5");
        await AcceptFromGiverAsync(ayla, Quest);

        var shared = await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = Quest });

        Assert.Contains(shared, m => m.Contains("\"type\":\"quest.shareResult\"") && m.Contains("\"result\":\"outOfReach\""));
        Assert.DoesNotContain(borinInbox, m => m.Contains("quest.shareOffered"));
    }

    [Fact]
    public async Task Sharing_AQuestNotHeld_IsRefused()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "qa6", "Ayla");
        var (borin, _) = await JoinAsync(hub, "qb6", "Borin");
        await PartyAsync(ayla, borin, "qb6");

        var shared = await SendAsync(ayla, WebMessageTypes.ShareQuest, 20, new { questId = Quest });

        Assert.Contains(shared, m => m.Contains("\"type\":\"protocol.error\""));
    }

    private static async Task PartyAsync(PlaySession leader, PlaySession member, string memberKey)
    {
        await SendAsync(leader, WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{memberKey}" });
        await SendAsync(member, WebMessageTypes.PartyAccept, 6, new { });
    }

    private static async Task AcceptFromGiverAsync(PlaySession session, uint questId)
    {
        var reply = await SendAsync(session, WebMessageTypes.QuestAccept, 10,
            new { networkId = "Creature:1", questId });
        Assert.Contains(reply, m => m.Contains("\"type\":\"quest.updated\""));
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, int level = 4, float x = 1)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, 3, level, [100, 200])]),
            Spawns, [], Content, combatRollUnit: () => 1.0, parties: TestSessions.PartiesOf(hub));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));
}
