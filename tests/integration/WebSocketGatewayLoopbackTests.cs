using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class WebSocketGatewayLoopbackTests
{
    private static int FreeLoopbackPort()
    {
        using var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task<string> ReceiveOneMessageAsync(ClientWebSocket client, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var result = await client.ReceiveAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task<string> ReceiveUntilAsync(ClientWebSocket client, string marker, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 20; i++)
        {
            var message = await ReceiveOneMessageAsync(client, cancellationToken);
            if (message.Contains(marker, StringComparison.Ordinal)) return message;
        }

        throw new TimeoutException($"No message containing \"{marker}\" arrived within 20 frames.");
    }

    [Fact]
    public async Task LoopbackSocket_LoginThenChooseCharacter_RoundTripsTheSameJsonTheBrowserSends()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);

        async Task SendAsync(string json) =>
            await client.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cts.Token);

        await SendAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var loginResponse = await ReceiveOneMessageAsync(client, cts.Token);
        Assert.Contains("account.loginResult", loginResponse);

        await SendAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        var chooseResponse = await ReceiveUntilAsync(client, "world.entered", cts.Token);
        Assert.Contains("world.entered", chooseResponse);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
    }

    [Fact]
    public async Task LoopbackSocket_ReturningToTheCharacterList_LetsAnotherCharacterInWithoutSigningInAgain()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla"), Character("char-b", 1, "Bryn")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        string Envelope(string type, int sequence, object payload) =>
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload));

        await SendAsync(client, Envelope(WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret")), cts.Token);
        await ReceiveOneMessageAsync(client, cts.Token);
        await SendAndReceiveUntilAsync(client,
            Envelope(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla")), "world.entered", cts.Token);

        var returned = await SendAndReceiveUntilAsync(client,
            Envelope(WebMessageTypes.CharactersReturn, 2, new { }), WebMessageTypes.CharactersReturned, cts.Token);
        var listed = await SendAndReceiveUntilAsync(client,
            Envelope(WebMessageTypes.CharactersList, 3, new { }), WebMessageTypes.CharactersListed, cts.Token);
        Assert.Contains("Bryn", listed);
        Assert.True(SequenceOf(listed) > SequenceOf(returned));

        var entered = await SendAndReceiveUntilAsync(client,
            Envelope(WebMessageTypes.CharactersChoose, 4, new ChooseCharacterRequest("Bryn")), "world.entered", cts.Token);
        Assert.Contains("player:char-b", entered);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
    }

    [Fact]
    public async Task LoopbackSocket_ReturningToTheCharacterList_IsRefusedWithNobodyInTheWorld()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        await SendAsync(client, WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), cts.Token);
        await ReceiveOneMessageAsync(client, cts.Token);

        var refused = await SendAndReceiveUntilAsync(client, WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersReturn, 1, new { })), WebMessageTypes.ProtocolError, cts.Token);
        Assert.Contains("invalid_state", refused);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
    }

    private static int SequenceOf(string message) =>
        JsonDocument.Parse(message).RootElement.GetProperty("sequence").GetInt32();

    [Fact]
    public async Task LoopbackSocket_ReconnectingAfterAPurchaseSeesItSurviveOnTheFileBackedStore()
    {
        var inventoryDir = Directory.CreateTempSubdirectory("ubiquitous-reconnect-test-");
        try
        {
            var port = FreeLoopbackPort();
            var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
            await using var gateway = new WebSocketGateway(options, Sessions(
                new DevelopmentAccountStore("developer", "dev-secret"),
                new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
                [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
                new FileInventoryStore(inventoryDir.FullName)));
            gateway.Start();

            using (var ctsA = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var clientA = new ClientWebSocket())
            {
                await clientA.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ctsA.Token);
                await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsA.Token);
                await ReceiveOneMessageAsync(clientA, ctsA.Token);
                await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), ctsA.Token);
                await ReceiveUntilAsync(clientA, "world.entered", ctsA.Token);

                await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 100, count = 2 })), ctsA.Token);
                await ReceiveUntilAsync(clientA, "inventory.itemAdded", ctsA.Token);

                await clientA.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ctsA.Token);
                await Task.Delay(500);
            }

            using var ctsB = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var clientB = new ClientWebSocket();
            await clientB.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ctsB.Token);
            await SendAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsB.Token);
            await ReceiveOneMessageAsync(clientB, ctsB.Token);
            var entered = await SendAndReceiveUntilAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), "inventory.updated", ctsB.Token);

            Assert.Contains("\"entry\":100", entered);
            Assert.Contains("\"count\":2", entered);
        }
        finally
        {
            inventoryDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoopbackSocket_ReconnectingAfterMovingSeesTheNewPositionSurviveOnTheFileBackedStore()
    {
        var characterDir = Directory.CreateTempSubdirectory("ubiquitous-reconnect-character-test-");
        try
        {
            var characterPath = Path.Combine(characterDir.FullName, "characters.json");
            var seed = Character("char-a", 1, "Ayla");
            await File.WriteAllTextAsync(characterPath, JsonSerializer.Serialize(
                new[] { seed }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var port = FreeLoopbackPort();
            var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
            await using var gateway = new WebSocketGateway(options, Sessions(
                new DevelopmentAccountStore("developer", "dev-secret"),
                FileCharacterStore.Load(characterPath),
                [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]));
            gateway.Start();

            using (var ctsA = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var clientA = new ClientWebSocket())
            {
                await clientA.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ctsA.Token);
                await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsA.Token);
                await ReceiveOneMessageAsync(clientA, ctsA.Token);
                await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), ctsA.Token);
                await ReceiveUntilAsync(clientA, "world.entered", ctsA.Token);

                await SendAndReceiveUntilAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                    WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(2, 2, 3, 0.5, true, MovementState.Walking))),
                    "entity.moved", ctsA.Token);

                await clientA.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ctsA.Token);
                await Task.Delay(500);
            }

            using var ctsB = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var clientB = new ClientWebSocket();
            await clientB.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ctsB.Token);
            await SendAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsB.Token);
            await ReceiveOneMessageAsync(clientB, ctsB.Token);
            var entered = await SendAndReceiveUntilAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), "world.entered", ctsB.Token);

            Assert.Contains("\"x\":2", entered);
            Assert.Contains("\"y\":2", entered);
            Assert.Contains("\"z\":3", entered);
        }
        finally
        {
            characterDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoopbackSocket_ClosesWithPolicyViolationWhenNothingIsEverSent()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])),
            idleTimeout: TimeSpan.FromMilliseconds(150));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);

        var buffer = new byte[16];
        var result = await client.ReceiveAsync(buffer, cts.Token);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, client.CloseStatus);
        Assert.Equal("Idle timeout", client.CloseStatusDescription);
    }

    [Fact]
    public async Task LoopbackSocket_RefusesAConnectionOverTheCap()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port, MaxConnections = 1 };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = new ClientWebSocket();
        await first.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        using var second = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(() => second.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token));

        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        await Task.Delay(300);
        using var third = new ClientWebSocket();
        await third.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
    }

    [Fact]
    public async Task LoopbackSocket_AnswersMessagesOverTheRateWithARateLimitedError()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port, MaxMessagesPerSecond = 5 };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        for (var sequence = 0; sequence < 20; sequence++)
            await SendAsync(client, WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.SessionPing, sequence, new { })), cts.Token);

        var limited = await ReceiveUntilAsync(client, "rate_limited", cts.Token);
        Assert.Contains(WebMessageTypes.ProtocolError, limited);
    }

    [Fact]
    public async Task LoopbackSocket_RefusesAnOriginNotOnTheList()
    {
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with
        {
            BindAddress = "127.0.0.1",
            WebSocketPort = port,
            AllowedOrigins = ["https://play.example"],
        };
        await using var gateway = new WebSocketGateway(options, Sessions(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")])));
        gateway.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stranger = new ClientWebSocket();
        stranger.Options.SetRequestHeader("Origin", "https://elsewhere.example");
        await Assert.ThrowsAsync<WebSocketException>(() => stranger.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token));

        using var allowed = new ClientWebSocket();
        allowed.Options.SetRequestHeader("Origin", "https://play.example");
        await allowed.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
    }

    private static Task SendAsync(ClientWebSocket client, string json, CancellationToken cancellationToken) =>
        client.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<string> SendAndReceiveUntilAsync(
        ClientWebSocket client, string json, string marker, CancellationToken cancellationToken)
    {
        await SendAsync(client, json, cancellationToken);
        return await ReceiveUntilAsync(client, marker, cancellationToken);
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);

    private static PlaySessionFactory Sessions(
        IAccountStore accounts,
        ICharacterStore characters,
        IReadOnlyList<CreatureSpawnDefinition>? creatureSpawns = null,
        Ubiquitous.Common.IInventoryStore? inventory = null) =>
        new(new SessionStores(new AccountAuthenticator(accounts, iterations: TestSessions.TestPasswordIterations), characters, Inventory: inventory),
            ServerContent.Empty with { CreatureSpawns = creatureSpawns ?? [] },
            new SharedWorld());
}
