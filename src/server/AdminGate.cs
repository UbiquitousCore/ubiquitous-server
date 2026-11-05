using System.Security.Cryptography;
using System.Text;

namespace Ubiquitous.Server;

// What the admin page may ask the game server: who is in the world, and to
// stop so Docker starts it again. Only a caller holding the token file's
// contents may ask; without a token file nobody can.
public sealed class AdminGate
{
    public const string TokenHeader = "X-Ubiquitous-Admin-Token";

    private readonly byte[]? _token;
    private readonly TaskCompletionSource _restartRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AdminGate(string? token) =>
        _token = string.IsNullOrWhiteSpace(token) ? null : Encoding.UTF8.GetBytes(token.Trim());

    public static AdminGate FromFile(string? path) =>
        new(!string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : null);

    public bool Enabled => _token is not null;

    public Task RestartRequested => _restartRequested.Task;

    public bool Allows(string? presented) =>
        _token is not null && !string.IsNullOrEmpty(presented)
        && CryptographicOperations.FixedTimeEquals(_token, Encoding.UTF8.GetBytes(presented.Trim()));

    public bool TryRequestRestart(string? presented)
    {
        if (!Allows(presented)) return false;
        _restartRequested.TrySetResult();
        return true;
    }
}
