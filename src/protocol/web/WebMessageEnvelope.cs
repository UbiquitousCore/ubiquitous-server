using System.Text.Json;

namespace Ubiquitous.Protocol.Web;

public sealed record WebMessageEnvelope(string Type, long Sequence, JsonElement Payload)
{
    public static WebMessageEnvelope Create(string type, long sequence, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        var raw = payload is null
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(payload, WebMessageJson.Options);

        if (raw.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The message payload must be a JSON object.", nameof(payload));
        }

        return new WebMessageEnvelope(type, sequence, raw);
    }

    internal static void Validate(WebMessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateType(message.Type);
        ValidateSequence(message.Sequence);

        if (message.Payload.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Message payload must be a JSON object.");
        }
    }

    private static void ValidateType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new JsonException("Message type must be a non-empty string.");
        }
    }

    private static void ValidateSequence(long sequence)
    {
        if (sequence < 0)
        {
            throw new JsonException("Message sequence must be a non-negative integer.");
        }
    }
}
