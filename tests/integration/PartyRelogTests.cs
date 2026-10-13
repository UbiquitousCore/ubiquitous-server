using System.Text.Json;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PartyRelogTests
{
    [Fact]
    public async Task ARelog_KeepsThePartyOnBothSides()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "rl-a1", "Ayla");
        var borin = await JoinAsync(hub, "rl-b1", "Borin");
        await FormPartyAsync(ayla, borin, "rl-b1");

        await hub.DisconnectAsync(borin.Session);
        ayla.Inbox.Clear();
        var back = await JoinAsync(hub, "rl-b1", "Borin");

        var own = LastRoster(back.EntryReplies);
        Assert.Equal(["Ayla", "Borin"], own.Select(m => m.Name));
        Assert.All(own, m => Assert.True(m.Online));
        var seen = LastRoster(ayla.Inbox);
        Assert.Contains(seen, m => m.Name == "Borin" && m.Online);
    }

    [Fact]
    public async Task ADisconnect_ShowsTheMemberOfflineToEveryoneElse()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "rl-a2", "Ayla");
        var borin = await JoinAsync(hub, "rl-b2", "Borin");
        var cara = await JoinAsync(hub, "rl-c2", "Cara");
        await FormPartyAsync(ayla, borin, "rl-b2");
        await FormPartyAsync(ayla, cara, "rl-c2");
        ayla.Inbox.Clear();
        borin.Inbox.Clear();

        await hub.DisconnectAsync(cara.Session);

        foreach (var inbox in new[] { ayla.Inbox, borin.Inbox })
        {
            var roster = LastRoster(inbox);
            Assert.Equal(["Ayla", "Borin", "Cara"], roster.Select(m => m.Name));
            Assert.Contains(roster, m => m.Name == "Cara" && !m.Online);
            Assert.Contains(roster, m => m.Name == "Borin" && m.Online);
        }
    }

    [Fact]
    public async Task TheLeaderRelogging_KeepsTheLead()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "rl-a3", "Ayla");
        var borin = await JoinAsync(hub, "rl-b3", "Borin");
        var cara = await JoinAsync(hub, "rl-c3", "Cara");
        await FormPartyAsync(ayla, borin, "rl-b3");
        await FormPartyAsync(ayla, cara, "rl-c3");

        await hub.DisconnectAsync(ayla.Session);
        Assert.Contains(LastRoster(borin.Inbox), m => m.Name == "Ayla" && m.IsLeader && !m.Online);

        var back = await JoinAsync(hub, "rl-a3", "Ayla");
        Assert.Contains(LastRoster(back.EntryReplies), m => m.Name == "Ayla" && m.IsLeader && m.Online);
        Assert.Contains(LastRoster(cara.Inbox), m => m.Name == "Ayla" && m.IsLeader && m.Online);

        var dara = await JoinAsync(hub, "rl-d3", "Dara");
        var invite = await SendAsync(back.Session, WebMessageTypes.PartyInvite, 9, new { networkId = "player:char-rl-d3" });
        Assert.Contains(invite, m => m.Contains("\"type\":\"party.inviteSent\""));
        Assert.Contains(dara.Inbox, m => m.Contains("\"type\":\"party.invited\""));
    }

    [Fact]
    public async Task AReloadClosingTheOldSessionLate_DoesNotShowTheMemberOffline()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "rl-a4", "Ayla");
        var borin = await JoinAsync(hub, "rl-b4", "Borin");
        await FormPartyAsync(ayla, borin, "rl-b4");

        var reloaded = await JoinAsync(hub, "rl-b4", "Borin");
        Assert.Contains(LastRoster(reloaded.EntryReplies), m => m.Name == "Ayla");
        ayla.Inbox.Clear();

        await hub.DisconnectAsync(borin.Session);

        Assert.DoesNotContain(ayla.Inbox, m => m.Contains("\"type\":\"party.roster\""));
        var cara = await JoinAsync(hub, "rl-c4", "Cara");
        await FormPartyAsync(ayla, cara, "rl-c4");
        Assert.Contains(LastRoster(ayla.Inbox), m => m.Name == "Borin" && m.Online);
    }

    [Fact]
    public async Task EnteringTheWorldInNoParty_SendsNoRoster()
    {
        var hub = new PlaySessionHub();
        var loner = await JoinAsync(hub, "rl-e5", "Enna");
        Assert.DoesNotContain(loner.EntryReplies, m => m.Contains("\"type\":\"party.roster\""));
    }

    private sealed record Joined(PlaySession Session, List<string> Inbox, IReadOnlyList<string> EntryReplies);

    private sealed record Row(string Name, bool IsLeader, bool Online);

    private static async Task<Joined> JoinAsync(PlaySessionHub hub, string guid, string name)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name)]), parties: TestSessions.PartiesOf(hub));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });

        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"))));
        var entry = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));
        return new Joined(session, inbox, entry);
    }

    private static async Task FormPartyAsync(Joined leader, Joined member, string memberKey)
    {
        await SendAsync(leader.Session, WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{memberKey}" });
        await SendAsync(member.Session, WebMessageTypes.PartyAccept, 6, new { });
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static IReadOnlyList<Row> LastRoster(IEnumerable<string> messages)
    {
        var raw = messages.LastOrDefault(m => m.Contains("\"type\":\"party.roster\""));
        Assert.NotNull(raw);
        using var doc = JsonDocument.Parse(raw);
        return [.. doc.RootElement.GetProperty("payload").GetProperty("members").EnumerateArray()
            .Select(m => new Row(m.GetProperty("name").GetString()!,
                m.GetProperty("isLeader").GetBoolean(), m.GetProperty("online").GetBoolean()))];
    }

    private static CharacterRecord Character(string guid, string name) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 2, 1, 2, 3, 4, [100, 200]);
}
