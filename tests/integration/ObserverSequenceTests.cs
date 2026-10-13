using System.Collections.Concurrent;
using System.Text.Json;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ObserverSequenceTests
{
    private const int MovesPerSender = 200;

    [Fact]
    public async Task ObserverEvents_ArriveWithStrictlyIncreasingSequence_UnderConcurrentSenders()
    {
        var hub = new PlaySessionHub();
        var mover = await JoinAsync(hub, "seq-a", "Ayla");
        var otherMover = await JoinAsync(hub, "seq-b", "Borin");
        var observer = await JoinAsync(hub, "seq-c", "Cara");
        var seen = observer.Seen;
        while (seen.TryDequeue(out _)) { }

        var movers = new[] { mover, otherMover }.Select(side => Task.Run(async () =>
        {
            for (var move = 0; move < MovesPerSender; move++)
            {
                var x = 2 + (move % 5) * 0.1;
                await hub.SendInOrderAsync(side.Session, () => side.Session.HandleTextAsync(Serialize(
                    WebMessageTypes.PlayerMove, 100 + move,
                    new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking))));
            }
        }));
        var ticker = Task.Run(async () =>
        {
            for (var tick = 0; tick < MovesPerSender; tick++)
            {
                await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
                await Task.Yield();
            }
        });
        await Task.WhenAll(movers.Append(ticker));
        await hub.TickAsync(_ => []);

        var sequences = seen.Select(SequenceOf).ToArray();
        Assert.True(sequences.Length >= MovesPerSender, $"observer received only {sequences.Length} messages");
        for (var index = 1; index < sequences.Length; index++)
            Assert.True(sequences[index] > sequences[index - 1],
                $"sequence {sequences[index]} followed {sequences[index - 1]} at message {index}");
    }

    private sealed record Side(PlaySession Session, ConcurrentQueue<string> Seen);

    private static async Task<Side> JoinAsync(PlaySessionHub hub, string guid, string name)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 2, 2, 3, 4, [100, 200])]));
        var seen = new ConcurrentQueue<string>();
        hub.Register(session, message => { seen.Enqueue(message); return Task.CompletedTask; });
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"))));
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));
        return new Side(session, seen);
    }

    private static string Serialize(string type, int sequence, object payload) =>
        WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload));

    private static int SequenceOf(string message) =>
        JsonDocument.Parse(message).RootElement.GetProperty("sequence").GetInt32();
}
