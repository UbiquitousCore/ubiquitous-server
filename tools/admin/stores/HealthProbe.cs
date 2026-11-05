using System.Net.Http.Json;

namespace Ubiquitous.Admin.Stores;

// Asks the game server's /health, the same check Docker makes.
public sealed class HealthProbe(HttpClient http, Uri healthUrl) : IServerStatus
{
    private sealed record Readiness(bool ProcessUp, bool DataReady, string? Reason);

    public async Task<ServerStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(healthUrl, cancellationToken);
            var readiness = await response.Content.ReadFromJsonAsync<Readiness>(cancellationToken);
            if (readiness is null) return new ServerStatus(ServerState.Unreachable, "The server sent no status.");
            return readiness.DataReady
                ? new ServerStatus(ServerState.Running)
                : new ServerStatus(ServerState.WaitingForData, readiness.Reason);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ServerStatus(ServerState.Unreachable, "The game server did not answer in time. If Docker itself is not responding, restart Docker.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new ServerStatus(ServerState.Unreachable, ex.Message);
        }
    }
}
