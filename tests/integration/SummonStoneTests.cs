using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SummonStoneTests
{
    private const uint Stone = 9103;
    private const string StoneId = "StaticObject:1";

    private static readonly GameContent Content = GameContent.Development with
    {
        SummonStoneEntries = new HashSet<uint> { Stone },
        Maps = new Dictionary<ushort, MapDefinition>
        {
            [40] = new(40, "dungeon", RecallMapId: 13, RecallX: 5, RecallY: 2, RecallZ: 5, MinLevel: 20, IsDungeon: true),
        },
    };

    private static readonly StaticObjectSpawnDefinition[] StoneSpawn = [new(1, 13, Stone, 2, 2, 4, 0)];

    [Fact]
    public async Task OpeningAStone_ListsTheOtherMembersOfTheParty()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "sa1", "Ayla");
        var (borin, _) = await JoinAsync(hub, "sb1", "Borin", x: 400);
        await PartyAsync(ayla, borin, "sb1");

        var opened = await SendAsync(ayla, WebMessageTypes.SummonOpen, 10, new { networkId = StoneId });

        var list = Assert.Single(opened, m => m.Contains("\"type\":\"summon.opened\""));
        Assert.Contains("\"networkId\":\"player:char-sb1\"", list);
        Assert.Contains("\"name\":\"Borin\"", list);
        Assert.Contains("\"canSummon\":true", list);
        Assert.DoesNotContain("char-sa1", list);
    }

    [Fact]
    public async Task OpeningAStone_OutOfAParty_ListsNobody()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "sa2", "Ayla");

        var opened = await SendAsync(ayla, WebMessageTypes.SummonOpen, 10, new { networkId = StoneId });

        Assert.Contains(opened, m => m.Contains("\"type\":\"summon.opened\"") && m.Contains("\"members\":[]"));
    }

    [Fact]
    public async Task OpeningAStone_FromTooFarAway_IsRefused()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "sa3", "Ayla", x: 400);

        var opened = await SendAsync(ayla, WebMessageTypes.SummonOpen, 10, new { networkId = StoneId });

        Assert.DoesNotContain(opened, m => m.Contains("summon.opened"));
    }

    [Fact]
    public async Task AcceptingASummon_MovesTheMemberToTheSummoner_AndTellsTheSummoner()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "sa4", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "sb4", "Borin", x: 400);
        await PartyAsync(ayla, borin, "sb4");

        var sent = await SendAsync(ayla, WebMessageTypes.SummonRequest, 10,
            new { networkId = StoneId, memberId = "player:char-sb4" });

        Assert.Contains(sent, m => m.Contains("\"type\":\"summon.result\"") && m.Contains("\"result\":\"asked\""));
        var asked = Assert.Single(borinInbox, m => m.Contains("\"type\":\"summon.asked\""));
        Assert.Contains("\"summonerName\":\"Ayla\"", asked);
        Assert.Contains("\"map\":13", asked);

        await SendAsync(borin, WebMessageTypes.SummonAnswer, 11, new { accept = true });

        Assert.Equal(1f, borin.PlayerPosition!.Value.X);
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"summon.result\"") && m.Contains("\"result\":\"accepted\""));
    }

    [Fact]
    public async Task TheSummoner_SeesTheMemberArriveBesideThem()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "sa8", "Ayla");
        var (borin, _) = await JoinAsync(hub, "sb8", "Borin", x: 400);
        await PartyAsync(ayla, borin, "sb8");
        await hub.SendInOrderAsync(borin, () => Task.FromResult<IReadOnlyList<string>>([]));
        await hub.SendInOrderAsync(ayla, () => SendAsync(ayla, WebMessageTypes.SummonRequest, 10,
            new { networkId = StoneId, memberId = "player:char-sb8" }));
        aylaInbox.Clear();

        await hub.SendInOrderAsync(borin, () => SendAsync(borin, WebMessageTypes.SummonAnswer, 11, new { accept = true }));

        var spawned = Assert.Single(aylaInbox, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("player:char-sb8"));
        Assert.Contains("\"x\":1", spawned);
    }

    [Fact]
    public async Task AMemberBelowTheDungeonsLevel_IsNotSummoned_AndBothAreTold()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "sa7", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "sb7", "Borin", x: 400, level: 19);
        await PartyAsync(ayla, borin, "sb7");

        var sent = await SendAsync(ayla, WebMessageTypes.SummonRequest, 10,
            new { networkId = StoneId, memberId = "player:char-sb7" });

        Assert.Contains(sent, m => m.Contains("\"type\":\"summon.result\"") && m.Contains("\"result\":\"levelTooLow\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"summon.tooLow\"") && m.Contains("\"summonerName\":\"Ayla\""));
        Assert.DoesNotContain(borinInbox, m => m.Contains("summon.asked"));
        var answered = await SendAsync(borin, WebMessageTypes.SummonAnswer, 11, new { accept = true });
        Assert.Contains(answered, m => m.Contains("\"type\":\"protocol.error\""));
        Assert.Equal(400f, borin.PlayerPosition!.Value.X);
    }

    [Fact]
    public async Task RefusingASummon_LeavesTheMemberWhereTheyAre_AndTellsTheSummoner()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "sa5", "Ayla");
        var (borin, _) = await JoinAsync(hub, "sb5", "Borin", x: 400);
        await PartyAsync(ayla, borin, "sb5");
        await SendAsync(ayla, WebMessageTypes.SummonRequest, 10, new { networkId = StoneId, memberId = "player:char-sb5" });

        await SendAsync(borin, WebMessageTypes.SummonAnswer, 11, new { accept = false });

        Assert.Equal(400f, borin.PlayerPosition!.Value.X);
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"summon.result\"") && m.Contains("\"result\":\"refused\""));
        var again = await SendAsync(borin, WebMessageTypes.SummonAnswer, 12, new { accept = true });
        Assert.Contains(again, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task Summoning_SomeoneOutsideTheParty_IsRefused()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "sa6", "Ayla");
        var (_, borinInbox) = await JoinAsync(hub, "sb6", "Borin", x: 400);

        var sent = await SendAsync(ayla, WebMessageTypes.SummonRequest, 10,
            new { networkId = StoneId, memberId = "player:char-sb6" });

        Assert.Contains(sent, m => m.Contains("\"type\":\"protocol.error\""));
        Assert.DoesNotContain(borinInbox, m => m.Contains("summon.asked"));
    }

    private static async Task PartyAsync(PlaySession leader, PlaySession member, string memberKey)
    {
        await SendAsync(leader, WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{memberKey}" });
        await SendAsync(member, WebMessageTypes.PartyAccept, 6, new { });
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, float x = 1, int level = 20)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, 3, level, [100, 200])]),
            staticObjectSpawns: StoneSpawn, content: Content, parties: TestSessions.PartiesOf(hub));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));
}
