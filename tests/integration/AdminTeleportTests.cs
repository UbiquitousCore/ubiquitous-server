using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AdminTeleportTests
{
    private static readonly GameContent Content = GameContent.Development with
    {
        Maps = new Dictionary<ushort, MapDefinition>
        {
            [13] = new(13, "Storm Wind"),
            [6] = new(6, "Under World"),
        },
    };

    [Fact]
    public async Task AnAdministrator_IsMovedToTheMapAndPlaceAsked_AndStaysThere()
    {
        var characters = new InMemoryCharacterStore([Character()]);
        var session = await EnterAsync(characters, admin: true);

        var moved = await TeleportAsync(session, new { mapId = 6, x = -860f, y = 20f, z = -6.5f });
        await session.SaveCharacterAsync();
        var saved = await characters.GetByGuidAsync("char-a");

        Assert.Contains(moved, message => message.Contains($"\"type\":\"{WebMessageTypes.WorldEntered}\""));
        Assert.Equal((ushort)6, saved!.MapId);
        Assert.Equal(-860f, saved.X);
        Assert.Equal(-6.5f, saved.Z);
    }

    [Fact]
    public async Task AnyoneElse_IsRefused_AndStaysWhereTheyAre()
    {
        var characters = new InMemoryCharacterStore([Character()]);
        var session = await EnterAsync(characters, admin: false);

        var refused = await TeleportAsync(session, new { mapId = 6, x = -860f, z = -6.5f });
        await session.SaveCharacterAsync();

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.Equal((ushort)13, (await characters.GetByGuidAsync("char-a"))!.MapId);
    }

    [Fact]
    public async Task AMapTheServerDoesNotKnow_IsRefused()
    {
        var session = await EnterAsync(new InMemoryCharacterStore([Character()]), admin: true);

        var refused = await TeleportAsync(session, new { mapId = 999, x = 0f, z = 0f });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidTeleport));
    }

    private static CharacterRecord Character() => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 12, [100, 200]);

    private static async Task<PlaySession> EnterAsync(ICharacterStore characters, bool admin)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin), characters, content: Content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> TeleportAsync(PlaySession session, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AdminTeleport, 2, payload)));
}
