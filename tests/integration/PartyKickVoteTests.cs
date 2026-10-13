using System.Text.Json;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PartyKickVoteTests
{
    private sealed record Side(PlaySession Session, List<string> Inbox, string Id);

    [Fact]
    public async Task ANonLeadersRequest_AsksTheOthers_AndPassesOnTheLeadersYes()
    {
        var (hub, leader, asker, target) = await TeamOfThreeAsync("v1");

        var reply = await SendAsync(hub, asker, WebMessageTypes.PartyVoteKick, new { networkId = target.Id });
        Assert.DoesNotContain(reply, m => m.Contains("\"type\":\"protocol.error\""));
        await TickAsync(hub);

        var asked = Assert.Single(PayloadsOf(leader.Inbox, WebMessageTypes.PartyVoteKickAsked));
        Assert.Equal(asker.Id, asked.GetProperty("initiatorId").GetString());
        Assert.Equal(target.Id, asked.GetProperty("targetId").GetString());
        Assert.Empty(PayloadsOf(target.Inbox, WebMessageTypes.PartyVoteKickAsked));
        Assert.Empty(PayloadsOf(asker.Inbox, WebMessageTypes.PartyVoteKickAsked));

        await SendAsync(hub, leader, WebMessageTypes.PartyVoteKickAnswer, new { agree = true });
        await TickAsync(hub);

        Assert.Single(PayloadsOf(target.Inbox, WebMessageTypes.PartyKicked));
        Assert.Contains(PayloadsOf(target.Inbox, WebMessageTypes.PartyRoster),
            roster => roster.GetProperty("members").GetArrayLength() == 0);
        foreach (var survivor in new[] { leader, asker })
        {
            var ended = Assert.Single(PayloadsOf(survivor.Inbox, WebMessageTypes.PartyVoteKickEnded));
            Assert.True(ended.GetProperty("passed").GetBoolean());
            Assert.Equal(target.Id, ended.GetProperty("targetId").GetString());
            var roster = PayloadsOf(survivor.Inbox, WebMessageTypes.PartyRoster).Last();
            Assert.Equal(2, roster.GetProperty("members").GetArrayLength());
            Assert.DoesNotContain(target.Id, roster.GetRawText());
        }
    }

    [Fact]
    public async Task ARefusal_IsSaidToTheAsker_AndLeavesTheTargetInTheTeam()
    {
        var (hub, leader, asker, target) = await TeamOfThreeAsync("v2");
        await SendAsync(hub, asker, WebMessageTypes.PartyVoteKick, new { networkId = target.Id });
        await TickAsync(hub);

        await SendAsync(hub, leader, WebMessageTypes.PartyVoteKickAnswer, new { agree = false });
        await TickAsync(hub);

        var refused = Assert.Single(PayloadsOf(asker.Inbox, WebMessageTypes.PartyVoteKickRefused));
        Assert.Equal(leader.Id, refused.GetProperty("memberId").GetString());
        Assert.Equal("Leader", refused.GetProperty("memberName").GetString());
        Assert.False(Assert.Single(PayloadsOf(asker.Inbox, WebMessageTypes.PartyVoteKickEnded)).GetProperty("passed").GetBoolean());
        Assert.False(Assert.Single(PayloadsOf(leader.Inbox, WebMessageTypes.PartyVoteKickEnded)).GetProperty("passed").GetBoolean());
        Assert.Empty(PayloadsOf(target.Inbox, WebMessageTypes.PartyKicked));
        Assert.Empty(PayloadsOf(target.Inbox, WebMessageTypes.PartyVoteKickEnded));
    }

    [Fact]
    public async Task InATeamOfFour_OneOtherYesIsEnough_AndTheLastQuestionIsWithdrawn()
    {
        var (hub, sides) = await TeamAsync("v3", "Leader", "Asker", "Other", "Target");
        var (leader, asker, other, target) = (sides[0], sides[1], sides[2], sides[3]);
        await SendAsync(hub, asker, WebMessageTypes.PartyVoteKick, new { networkId = target.Id });
        await TickAsync(hub);
        Assert.Single(PayloadsOf(other.Inbox, WebMessageTypes.PartyVoteKickAsked));

        await SendAsync(hub, other, WebMessageTypes.PartyVoteKickAnswer, new { agree = true });
        await TickAsync(hub);

        Assert.Single(PayloadsOf(target.Inbox, WebMessageTypes.PartyKicked));
        Assert.True(Assert.Single(PayloadsOf(leader.Inbox, WebMessageTypes.PartyVoteKickEnded)).GetProperty("passed").GetBoolean());
        var late = await SendAsync(hub, leader, WebMessageTypes.PartyVoteKickAnswer, new { agree = false });
        Assert.Contains(late, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task TheLeaderIsNotOfferedAVote()
    {
        var (hub, leader, _, target) = await TeamOfThreeAsync("v4");

        var reply = await SendAsync(hub, leader, WebMessageTypes.PartyVoteKick, new { networkId = target.Id });

        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task AVoteNobodyAnswers_RunsOutAsARefusal()
    {
        var (hub, leader, asker, target) = await TeamOfThreeAsync("v5");
        await SendAsync(hub, asker, WebMessageTypes.PartyVoteKick, new { networkId = target.Id });

        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(31)));

        Assert.Single(PayloadsOf(asker.Inbox, WebMessageTypes.PartyVoteKickRefused));
        Assert.False(Assert.Single(PayloadsOf(asker.Inbox, WebMessageTypes.PartyVoteKickEnded)).GetProperty("passed").GetBoolean());
        Assert.Empty(PayloadsOf(target.Inbox, WebMessageTypes.PartyKicked));
    }

    private static async Task<(PlaySessionHub Hub, Side Leader, Side Asker, Side Target)> TeamOfThreeAsync(string tag)
    {
        var (hub, sides) = await TeamAsync(tag, "Leader", "Asker", "Target");
        return (hub, sides[0], sides[1], sides[2]);
    }

    private static async Task<(PlaySessionHub Hub, Side[] Sides)> TeamAsync(string tag, params string[] names)
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore([.. names.Select(name => Character($"{tag}{name}", name))]);
        var sides = new List<Side>();
        foreach (var name in names)
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters, parties: TestSessions.PartiesOf(hub));
            var inbox = new List<string>();
            hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
            var side = new Side(session, inbox, $"player:char-{tag}{name}");
            await SendAsync(hub, side, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
            await SendAsync(hub, side, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
            sides.Add(side);
        }
        foreach (var joiner in sides.Skip(1))
        {
            await SendAsync(hub, sides[0], WebMessageTypes.PartyInvite, new { networkId = joiner.Id });
            await SendAsync(hub, joiner, WebMessageTypes.PartyAccept, new { });
        }
        await TickAsync(hub);
        foreach (var side in sides) side.Inbox.Clear();
        return (hub, [.. sides]);
    }

    private static Task TickAsync(PlaySessionHub hub) =>
        hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySessionHub hub, Side side, string type, object payload)
    {
        IReadOnlyList<string> replies = [];
        await hub.SendInOrderAsync(side.Session, async () => replies = await side.Session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload))));
        return replies;
    }

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static CharacterRecord Character(string guid, string name) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
