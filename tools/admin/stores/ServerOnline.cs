using System.Net.Http.Json;

namespace Ubiquitous.Admin.Stores;

// Asks the game server's /online, with the token both containers read from the secrets volume.
public sealed class ServerOnline(HttpClient http, Uri onlineUrl, string? tokenFile) : IOnlinePlayers
{
    public async Task<IReadOnlySet<string>?> NamesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenFile) || !File.Exists(tokenFile)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, onlineUrl);
        request.Headers.Add(ServerRestart.TokenHeader, (await File.ReadAllTextAsync(tokenFile, cancellationToken)).Trim());
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var names = await response.Content.ReadFromJsonAsync<string[]>(cancellationToken) ?? [];
            return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
