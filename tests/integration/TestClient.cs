using System.Text.Json;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

internal sealed class TestClient(PlaySession session)
{
    private const string DefaultUser = "developer";
    private const string DefaultPassword = "dev-secret";

    private int _sequence;

    public PlaySession Session => session;

    public async Task<Received> SendAsync(string type, object? payload = null)
    {
        var text = WebMessageJson.Serialize(WebMessageEnvelope.Create(type, _sequence++, payload ?? new { }));
        return Received.From(await session.HandleTextAsync(text));
    }

    public async Task EnterAsync(string characterName, string user = DefaultUser, string password = DefaultPassword)
    {
        await SendAsync(WebMessageTypes.AccountLogin, new LoginRequest(user, password));
        var entered = await SendAsync(WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(characterName));
        Assert.True(entered.Has(WebMessageTypes.WorldEntered), $"{characterName} did not enter the world");
    }

    public Received Tick(DateTimeOffset now) => Received.From(session.TickCombat(now));
}

internal sealed class Received(IReadOnlyList<WebMessageEnvelope> messages)
{
    public IReadOnlyList<WebMessageEnvelope> Messages => messages;

    public static Received From(IEnumerable<string> raw) =>
        new([.. raw.Select(WebMessageJson.Deserialize)]);

    public Received Plus(Received other) => new([.. messages, .. other.Messages]);

    public IReadOnlyList<WebMessageEnvelope> All(string type) =>
        [.. messages.Where(message => message.Type == type)];

    public WebMessageEnvelope Single(string type) => Assert.Single(All(type));

    public bool Has(string type) => messages.Any(message => message.Type == type);

    public bool Has(string type, string mentioning) =>
        All(type).Any(message => message.Mentions(mentioning));

    public static T Payload<T>(WebMessageEnvelope message) => WebMessageJson.DeserializePayload<T>(message.Payload);
}

internal static class EnvelopeExtensions
{
    public static bool Mentions(this WebMessageEnvelope message, string text) =>
        message.Payload.GetRawText().Contains(text, StringComparison.Ordinal);

    public static string? Text(this WebMessageEnvelope message, string property) =>
        message.Payload.ValueKind == JsonValueKind.Object
        && message.Payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
