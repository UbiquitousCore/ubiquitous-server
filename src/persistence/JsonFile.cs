using System.Text.Json;

namespace Ubiquitous.Persistence;

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static async Task<IReadOnlyList<T>> ReadEachAsync<T>(
        string directory, string pattern, CancellationToken cancellationToken)
        where T : class
    {
        if (!Directory.Exists(directory)) return [];
        var values = new List<T>();
        foreach (var path in Directory.EnumerateFiles(directory, pattern))
        {
            if (await ReadAsync<T>(path, cancellationToken) is { } value) values.Add(value);
        }

        return values;
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
