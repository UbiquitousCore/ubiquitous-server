using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

internal static class TravelSession
{
    internal static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1, 13, 9002, 2f, 2f, 3f, 0f),
        new(2, 13, 9001, 3.5f, 2f, 3f, 0f),
    ];

    internal static readonly StaticObjectSpawnDefinition[] StaticSpawns =
    [
        new(1, 13, 7001, 3f, 2f, 3f, 0f),
    ];

    internal static PlaySession Session(GameContent? content = null) => TestSessions.Create(
        new DevelopmentAccountStore("developer", "dev-secret"),
        new InMemoryCharacterStore([Character()]), Spawns, StaticSpawns, content);

    internal static async Task<PlaySession> LoggedInAsync(GameContent? content = null)
    {
        var session = Session(content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        return session;
    }

    internal static async Task<PlaySession> InWorldAsync(GameContent? content = null)
    {
        var session = await LoggedInAsync(content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    internal static CharacterRecord Character() => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
