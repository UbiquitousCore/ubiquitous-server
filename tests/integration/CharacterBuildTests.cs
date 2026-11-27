using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CharacterBuildTests
{
    private static DataBuild Build(string version) => new(version, new Dictionary<string, TableShape>());

    private static CharacterRecord Ayla(string? newest) =>
        new("char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 12, [100, 200]) { NewestGameVersion = newest };

    private static async Task<PlaySession> SignedInAsync(InMemoryCharacterStore store, string serverVersion)
    {
        var session = TestSessions.Create(new InMemoryAccountStore("developer", "dev-secret"), store,
            build: Build(serverVersion), developmentEntry: true);
        await Send(session, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        return session;
    }

    private static Task<IReadOnlyList<string>> Send(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload)));

    private static JsonElement PayloadOf(IEnumerable<string> replies, string type) =>
        JsonDocument.Parse(replies.Single(reply => reply.Contains($"\"type\":\"{type}\""))).RootElement.GetProperty("payload");

    [Theory]
    [InlineData("4.2.21", "4.2.21")]
    [InlineData("unknown", "1.1.4")]
    public async Task BeforeSigningIn_TheServerNamesTheGameVersionItPlays(string build, string announced)
    {
        var session = TestSessions.Create(new InMemoryAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([]), build: Build(build));

        var described = PayloadOf(await Send(session, WebMessageTypes.SessionDescribe, new { }),
            WebMessageTypes.SessionDescribed);

        Assert.Equal(announced, described.GetProperty("gameVersion").GetString());
    }

    [Fact]
    public async Task ACharacterPlayedOnALaterBuild_IsListedWithItAndRefused()
    {
        var session = await SignedInAsync(new InMemoryCharacterStore([Ayla("4.2.21")]), "2.2.0");

        var listed = PayloadOf(await Send(session, WebMessageTypes.CharactersList, new { }), WebMessageTypes.CharactersListed);
        Assert.Equal("4.2.21", listed.GetProperty("characters")[0].GetProperty("needsGameVersion").GetString());

        var chosen = PayloadOf(await Send(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla")),
            WebMessageTypes.CharactersChosen);
        Assert.False(chosen.GetProperty("ok").GetBoolean());
        Assert.Equal((int)CharacterChooseCode.NeedsLaterGameVersion, chosen.GetProperty("code").GetInt32());
        Assert.Contains("4.2.21", chosen.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task DevelopmentEntry_RefusesACharacterOfALaterBuildToo()
    {
        var session = TestSessions.Create(new InMemoryAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Ayla("2.2.0")]), build: Build("1.1.4"), developmentEntry: true);

        var chosen = PayloadOf(await Send(session, WebMessageTypes.DevEnterWorld, new { characterRef = "char-a" }),
            WebMessageTypes.CharactersChosen);

        Assert.False(chosen.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ACharacterOfAnEarlierBuild_PlaysAndIsSavedAsTheLaterOne()
    {
        var store = new InMemoryCharacterStore([Ayla("1.1.4")]);
        var session = await SignedInAsync(store, "4.2.21");

        var listed = PayloadOf(await Send(session, WebMessageTypes.CharactersList, new { }), WebMessageTypes.CharactersListed);
        Assert.False(listed.GetProperty("characters")[0].TryGetProperty("needsGameVersion", out var needs)
            && needs.ValueKind != JsonValueKind.Null);
        Assert.Contains(await Send(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla")),
            reply => reply.Contains("\"type\":\"world.entered\""));
        await session.SaveCharacterAsync();

        Assert.Equal("4.2.21", (await store.GetByGuidAsync("char-a"))!.NewestGameVersion);
    }

    [Fact]
    public async Task ACharacterMadeOnAServer_KeepsItsGameVersion()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedInAsync(store, "2.2.0");

        await Send(session, WebMessageTypes.CharactersCreate,
            new CreateCharacterRequest("Brin", 1, 1, 1, new Appearance(0, 0, 0, 0)));

        Assert.Equal("2.2.0", (await store.FindByNameAsync("Brin"))!.NewestGameVersion);
    }
}
