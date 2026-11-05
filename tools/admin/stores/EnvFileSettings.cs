namespace Ubiquitous.Admin.Stores;

// The .env beside docker-compose.yml. Every line it does not change, comments
// included, is written back as it was.
public sealed class EnvFileSettings(string path) : ISettingsStore
{
    public string Location => path;

    public IReadOnlyDictionary<string, string> Read()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadAllLines(path))
        {
            if (Split(line) is { } pair) values[pair.Name] = pair.Value;
        }

        return values;
    }

    public void Write(IReadOnlyDictionary<string, string?> changes) =>
        File.WriteAllText(path, Apply(File.Exists(path) ? File.ReadAllText(path) : "", changes));

    // A set value replaces the line that sets it, or else the commented line
    // showing it, or else goes at the end; a null removes the line that sets it.
    public static string Apply(string text, IReadOnlyDictionary<string, string?> changes)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        foreach (var (name, value) in changes)
        {
            var at = lines.FindIndex(line => Split(line)?.Name == name);
            if (value is null)
            {
                if (at >= 0) lines.RemoveAt(at);
                continue;
            }

            if (at < 0) at = lines.FindIndex(line => line.TrimStart().StartsWith("# " + name + "=", StringComparison.Ordinal));
            if (at >= 0) lines[at] = $"{name}={value}";
            else lines.Add($"{name}={value}");
        }

        return string.Join(newline, lines) + newline;
    }

    private static (string Name, string Value)? Split(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return null;
        var equals = trimmed.IndexOf('=', StringComparison.Ordinal);
        return equals <= 0 ? null : (trimmed[..equals].Trim(), trimmed[(equals + 1)..].Trim());
    }
}
