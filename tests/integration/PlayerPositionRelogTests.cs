using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerPositionRelogTests
{
    [Fact]
    public async Task ACharacterLoggedOut_ComesBackWhereItLeft()
    {
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]);
        var (first, _) = await EnterReportingAsync(characters);
        await Send(first, WebMessageTypes.PlayerMove,
            new PlayerMovePayload(3, 2, 4, 0, true, MovementState.Walking));
        await first.SaveProgressAsync();

        var again = await EnterReportingAsync(characters);
        var saved = await characters.GetByGuidAsync("char-a");

        Assert.Equal(3, saved!.X, 1);
        Assert.Equal(4, saved.Z, 1);
        Assert.Contains(again.Entered, m => m.Contains("\"type\":\"world.entered\"") && m.Contains("\"x\":3"));
    }

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered)> EnterReportingAsync(
        InMemoryCharacterStore characters)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters, []);
        await Send(session, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        var entered = await Send(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla"));
        return (session, entered);
    }

    private static Task<IReadOnlyList<string>> Send(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload)));
}
