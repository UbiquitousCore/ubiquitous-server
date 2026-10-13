using System.Text.Json;

namespace Ubiquitous.Server;

internal static class PayloadIds
{
    public static string? NetworkId(JsonElement payload) => Read(payload, "networkId");

    public static string? Read(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind == JsonValueKind.Number ? value.GetInt64().ToString() : null;
    }
}
