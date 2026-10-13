using System.Globalization;

namespace Ubiquitous.Modules;

public sealed class ModuleSettings
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public ModuleSettings(IReadOnlyDictionary<string, string>? values = null)
    {
        _values = (values ?? new Dictionary<string, string>())
            .ToDictionary(pair => Normalize(pair.Key), pair => pair.Value, StringComparer.Ordinal);
    }

    // Config keys are camelCase and environment keys UPPER_SNAKE; both name the same setting.
    public static string Normalize(string key) => key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    public string? Text(string key) => _values.TryGetValue(Normalize(key), out var value) ? value : null;

    public bool Flag(string key, bool fallback) => Text(key) switch
    {
        null => fallback,
        "1" => true,
        "0" => false,
        var value when bool.TryParse(value, out var parsed) => parsed,
        var value => throw new InvalidDataException($"Module setting {key} must be true or false, not '{value}'."),
    };

    public int Number(string key, int fallback) => Text(key) switch
    {
        null => fallback,
        var value when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        var value => throw new InvalidDataException($"Module setting {key} must be a whole number, not '{value}'."),
    };
}
