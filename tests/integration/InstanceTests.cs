using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class InstanceTests
{
    private static readonly CreatureSpawnDefinition[] DungeonCreatures = [new(1, 20, 9500, 500, 500, 0, 0)];

    [Fact]
    public async Task TwoUngroupedPlayersEnteringTheSameMap_GetIsolatedCopies()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var (ayla, _) = await JoinAsync(hub, instances, "insta1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, instances, "instb1", "Borin");

        await SendAsync(hub, ayla, WebMessageTypes.InstanceEnter, 10, new { mapId = 20 });
        await SendAsync(hub, borin, WebMessageTypes.InstanceEnter, 11, new { mapId = 20 });

        Assert.NotEqual(ayla.CurrentInstanceKey, borin.CurrentInstanceKey);

        Assert.DoesNotContain(borinInbox.Forwarded, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("char-insta1"));
    }

    [Fact]
    public async Task APartyEnteringTogether_LandsInTheSameInstanceCopy_AndSeesEachOther()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var (ayla, _) = await JoinAsync(hub, instances, "insta2", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, instances, "instb2", "Borin");
        await SendAsync(hub, ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-instb2" });
        await SendAsync(hub, borin, WebMessageTypes.PartyAccept, 6, new { });
        borinInbox.Forwarded.Clear();

        var aylaEnter = await SendAsync(hub, ayla, WebMessageTypes.InstanceEnter, 10, new { mapId = 20 });
        var borinEnter = await SendAsync(hub, borin, WebMessageTypes.InstanceEnter, 11, new { mapId = 20 });

        Assert.Equal(ayla.CurrentInstanceKey, borin.CurrentInstanceKey);
        Assert.Equal(ExtractCreatureNetworkId(aylaEnter), ExtractCreatureNetworkId(borinEnter));

        Assert.Contains(borinEnter, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("char-insta2"));
    }

    [Fact]
    public async Task InstanceStaysLiveWhileAnyMemberRemains_AndTearsDownOnceEmpty()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var (ayla, _) = await JoinAsync(hub, instances, "insta3", "Ayla");
        var (borin, _) = await JoinAsync(hub, instances, "instb3", "Borin");
        await SendAsync(hub, ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-instb3" });
        await SendAsync(hub, borin, WebMessageTypes.PartyAccept, 6, new { });

        await SendAsync(hub, ayla, WebMessageTypes.InstanceEnter, 10, new { mapId = 20 });
        await SendAsync(hub, borin, WebMessageTypes.InstanceEnter, 11, new { mapId = 20 });
        var key = ayla.CurrentInstanceKey!;
        Assert.True(instances.IsLive(key));

        ayla.LeaveInstanceIfAny();
        Assert.True(instances.IsLive(key), "the instance must survive while Borin is still in it");

        borin.LeaveInstanceIfAny();
        Assert.True(instances.IsLive(key), "an emptied instance is held for its grace, so a relog rejoins the same run");
        Assert.False(instances.IsLive(key, DateTimeOffset.UtcNow + InstanceService.EmptyGrace),
            "the instance must be torn down once its grace runs out with nobody back");
    }

    [Fact]
    public async Task EnteringAnInstance_ThenAnotherMapChange_LeavesTheFirstInstance()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var (ayla, _) = await JoinAsync(hub, instances, "insta4", "Ayla");

        await SendAsync(hub, ayla, WebMessageTypes.InstanceEnter, 10, new { mapId = 20 });
        var firstKey = ayla.CurrentInstanceKey!;
        Assert.True(instances.IsLive(firstKey));

        await SendAsync(hub, ayla, WebMessageTypes.InstanceEnter, 11, new { mapId = 21 });

        Assert.NotEqual(firstKey, ayla.CurrentInstanceKey);
        Assert.False(instances.IsLive(firstKey, DateTimeOffset.UtcNow + InstanceService.EmptyGrace));
    }

    private static string ExtractCreatureNetworkId(IEnumerable<string> responses)
    {
        var spawned = responses.Single(r => r.Contains("\"type\":\"entity.spawned\"") && r.Contains("\"entry\""));
        const string marker = "\"networkId\":\"";
        var start = spawned.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = spawned.IndexOf('"', start);
        return spawned[start..end];
    }

    [Fact]
    public async Task EnteringAnInstanceOfAnUnknownMap_IsRefused_AndLeavesTheSessionWhereItWas()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var content = GameContent.Development with
        {
            Maps = new Dictionary<ushort, MapDefinition> { [13] = new(13, "test") },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("insta5", "p"),
            new InMemoryCharacterStore([Character("insta5", "Ayla")]),
            creatureSpawns: DungeonCreatures, content: content, instances: instances);
        hub.Register(session, _ => Task.CompletedTask);
        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest("insta5", "p"));
        await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest("Ayla"));

        var responses = await SendAsync(hub, session, WebMessageTypes.InstanceEnter, 10, new { mapId = 54 });

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\"") && r.Contains("invalid_instance"));
        Assert.DoesNotContain(responses, r => r.Contains("\"type\":\"world.entered\""));
        Assert.Null(session.CurrentInstanceKey);
    }

    private sealed record Inbox(List<string> Direct, List<string> Forwarded);

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, InstanceService instances, string guid, string name)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name)]),
            creatureSpawns: DungeonCreatures, instances: instances, parties: TestSessions.PartiesOf(hub));
        var inbox = new Inbox([], []);
        hub.Register(session, message => { inbox.Forwarded.Add(message); return Task.CompletedTask; });

        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        inbox.Direct.AddRange(await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name)));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySessionHub hub, PlaySession session, string type, int sequence, object payload) =>
        await SendRawAsync(hub, session, type, sequence, payload);

    private static async Task<IReadOnlyList<string>> SendRawAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        var presence = await hub.BroadcastAsync(session, responses);
        return [.. responses, .. presence];
    }

    private static CharacterRecord Character(string guid, string name) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
