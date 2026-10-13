using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DevEnterWorldTests
{
    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    [Fact]
    public async Task EntersTheWorldDirectlyByCharacterGuidWithNoPriorLogin()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        Assert.Contains(responses, message => message.Contains("\"type\":\"world.entered\"")
            && message.Contains("player:char-a"));
        Assert.Equal((ushort)13, session.CurrentMapId);
    }

    [Fact]
    public async Task IsRefusedWhenDevelopmentEntryIsNotEnabled()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [])]));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        Assert.Contains("\"forbidden\"", Assert.Single(responses));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"world.entered\""));
    }

    [Fact]
    public async Task RefusesAReferenceThatMatchesNoCharacter()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "no-such-guid" })));

        Assert.Contains("invalid_state", Assert.Single(response));
        Assert.Null(session.CurrentMapId);
    }

    [Fact]
    public async Task RefusesAMissingCharacterReference()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { })));

        Assert.Contains("invalid_state", Assert.Single(response));
    }

    [Fact]
    public async Task RefusesASecondAttemptOnceACharacterIsAlreadySelected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 1, new { characterRef = "char-a" })));

        Assert.Contains("invalid_state", Assert.Single(response));
    }

    [Fact]
    public async Task ANormalLoginAfterwardIsRefusedTheSameWayARepeatLoginIs()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 1, new LoginRequest("developer", "dev-secret"))));

        Assert.Contains("invalid_state", response.Single());
    }

    [Fact]
    public async Task GameplayWorksNormallyAfterEnteringThisWay()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcTalk, 1, new { networkId = "Creature:1" })));

        Assert.Contains("npc.gossip", Assert.Single(response));
    }
}
