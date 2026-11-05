namespace Ubiquitous.Admin.Stores;

// Posts to the game server's /restart with the token both containers read
// from the secrets volume.
public sealed class ServerRestart(HttpClient http, Uri restartUrl, string? tokenFile) : IServerControl
{
    public const string TokenHeader = "X-Ubiquitous-Admin-Token";

    public async Task<bool> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenFile) || !File.Exists(tokenFile)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Post, restartUrl);
        request.Headers.Add(TokenHeader, (await File.ReadAllTextAsync(tokenFile, cancellationToken)).Trim());
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
