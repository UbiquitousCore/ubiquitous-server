using System.Text.Json;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed record OutboundEvent(
    string Type,
    JsonElement Payload,
    string? NetworkId,
    string? ChatChannel,
    IReadOnlyList<string> MentionedPlayerIds)
{
    private const string PlayerIdPrefix = "player:";

    public static OutboundEvent? From(string raw)
    {
        try
        {
            var envelope = WebMessageJson.Deserialize(raw);
            return Describe(envelope.Type, envelope.Payload);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    public static OutboundEvent Of(WebMessageEnvelope envelope) => Describe(envelope.Type, envelope.Payload);

    public static OutboundEvent Of(string type, object payload) =>
        Describe(type, WebMessageJson.ToElement(payload));

    private static OutboundEvent Describe(string type, JsonElement payload)
    {
        var mentioned = new List<string>();
        CollectPlayerIds(payload, mentioned);
        return new OutboundEvent(type, payload, StringProperty(payload, "networkId"),
            StringProperty(payload, "channel"), mentioned);
    }

    private static string? StringProperty(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void CollectPlayerIds(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString() is { } text && text.StartsWith(PlayerIdPrefix, StringComparison.Ordinal))
                    found.Add(text);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectPlayerIds(property.Value, found);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectPlayerIds(item, found);
                break;
        }
    }
}
