namespace Ubiquitous.Modules;

// A connection with no socket: what a module sends is handled exactly as a
// client's message, and what the server would write to the socket queues here.
public interface IClientConnection : IAsyncDisposable
{
    bool IsOpen { get; }

    Task SendAsync(string message, CancellationToken cancellationToken = default);

    IReadOnlyList<string> TakeReceived();
}
