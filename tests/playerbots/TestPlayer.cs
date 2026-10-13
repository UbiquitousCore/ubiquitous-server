using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Playerbots.Tests;

// A person at a client, reaching the server the way the bots do.
internal sealed class TestPlayer(LocalConnection connection)
{
    private long _sequence;

    public List<string> Received { get; } = [];

    public LocalConnection Connection => connection;

    public async Task SendAsync(string type, object payload)
    {
        await connection.SendAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, _sequence++, payload)));
        Collect();
    }

    public void Collect() => Received.AddRange(connection.TakeReceived());

    public static async Task<TestPlayer> EnterAsync(BotTestWorld world, string userId, string secret, string name)
    {
        var player = new TestPlayer(new LocalConnection(world.Sessions, world.Hub, messagesPerSecond: 30, playedByModule: false));
        await player.SendAsync(WebMessageTypes.AccountLogin, new LoginRequest(userId, secret));
        await player.SendAsync(WebMessageTypes.CharactersCreate,
            new CreateCharacterRequest(name, Ubiquitous.Data.PlayerRaceIds.Human, Ubiquitous.Data.PlayerClassIds.Warrior, 0, new Appearance(0, 0, 0, 0)));
        await player.SendAsync(WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
        return player;
    }
}
