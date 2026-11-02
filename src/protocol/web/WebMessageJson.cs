using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public static class WebMessageJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly JsonSerializerOptions RequestOptions = CreateOptions(respectNullableAnnotations: true);

    private static JsonSerializerOptions CreateOptions(bool respectNullableAnnotations = false)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.RespectNullableAnnotations = respectNullableAnnotations;
        options.RespectRequiredConstructorParameters = respectNullableAnnotations;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static JsonElement ToElement(object payload) =>
        JsonSerializer.SerializeToElement(payload, payload.GetType(), Options);

    public static string Serialize(WebMessageEnvelope message) =>
        JsonSerializer.Serialize(message, Options);

    public static string SerializePayload(JsonElement payload) =>
        JsonSerializer.Serialize(payload, Options);

    // What Serialize writes for an envelope, around a payload already written once for many recipients.
    public static string Serialize(string type, long sequence, string payloadJson)
    {
        var buffer = new ArrayBufferWriter<byte>(payloadJson.Length + type.Length + 48);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = Options.Encoder }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteNumber("sequence", sequence);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(payloadJson, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static WebMessageEnvelope Deserialize(string json)
    {
        var envelope = JsonSerializer.Deserialize<WebMessageEnvelope>(json, Options)
            ?? throw new JsonException("Message deserialized to null.");

        if (envelope.Payload.ValueKind == JsonValueKind.Null
            || envelope.Payload.ValueKind == JsonValueKind.Undefined)
        {
            envelope = envelope with { Payload = JsonSerializer.SerializeToElement(new { }) };
        }

        WebMessageEnvelope.Validate(envelope);
        return envelope;
    }

    public static T DeserializePayload<T>(JsonElement payload) =>
        payload.Deserialize<T>(RequestOptions)
        ?? throw new JsonException($"Payload could not be read as {typeof(T).Name}.");
}
