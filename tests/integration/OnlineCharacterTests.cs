using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class OnlineCharacterTests
{
    [Fact]
    public async Task SecondConnection_ChoosingACharacterAlreadyInTheWorld_IsRefused()
    {
        var (world, characters) = Server();
        var first = await LoggedInAsync(world, characters);
        var second = await LoggedInAsync(world, characters);

        await ChooseAsync(first, "Ayla");
        var refused = await ChooseAsync(second, "Ayla");

        var reply = Assert.Single(refused);
        Assert.Contains("\"type\":\"characters.chosen\"", reply);
        Assert.Contains("\"ok\":false", reply);
        Assert.Contains("already in the world", reply);
    }

    [Fact]
    public async Task AfterTheFirstConnectionLeaves_TheCharacterCanBeChosenAgain()
    {
        var (world, characters) = Server();
        var first = await LoggedInAsync(world, characters);
        var second = await LoggedInAsync(world, characters);
        await ChooseAsync(first, "Ayla");

        first.LeaveWorld();
        var entered = await ChooseAsync(second, "Ayla");

        Assert.Contains(entered, reply => reply.Contains("\"type\":\"world.entered\""));
    }

    [Fact]
    public async Task DevEnterWorld_ForACharacterAlreadyInTheWorld_IsRefused()
    {
        var (world, characters) = Server();
        var first = await LoggedInAsync(world, characters);
        var second = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            developmentEntry: true, world: world);
        await ChooseAsync(first, "Ayla");

        var refused = await SendAsync(second, WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-online-a" });

        var reply = Assert.Single(refused);
        Assert.Contains("already in the world", reply);
    }

    [Fact]
    public async Task ReturningToCharacterList_FreesTheCharacter()
    {
        var (world, characters) = Server();
        var first = await LoggedInAsync(world, characters);
        var second = await LoggedInAsync(world, characters);
        await ChooseAsync(first, "Ayla");

        await SendAsync(first, WebMessageTypes.CharactersReturn, 3, new { });
        Assert.True(first.LeftForCharacterList);
        first.LeaveWorld();
        var entered = await ChooseAsync(second, "Ayla");

        Assert.Contains(entered, reply => reply.Contains("\"type\":\"world.entered\""));
    }

    private static (SharedWorld World, InMemoryCharacterStore Characters) Server()
    {
        var characters = new InMemoryCharacterStore([Character("char-online-a", "Ayla"), Character("char-online-b", "Borin")]);
        var world = new SharedWorld(new SessionStores(new AccountAuthenticator(new DevelopmentAccountStore("developer", "dev-secret")), characters));
        return (world, characters);
    }

    private static async Task<PlaySession> LoggedInAsync(SharedWorld world, InMemoryCharacterStore characters)
    {
        var session = TestSessions.Builder().WithCharacters(characters).InWorld(world).Build();
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        return session;
    }

    private static Task<IReadOnlyList<string>> ChooseAsync(PlaySession session, string name) =>
        SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
