using Ubiquitous.Modules;

namespace Ubiquitous.Server;

// A client inside the process. It is held to the same session, delivery order
// and message rate as a socket, so what it can do a remote client could too.
public sealed class LocalConnection : IClientConnection
{
    private readonly PlaySessionFactory _factory;
    private readonly PlaySessionHub _hub;
    private readonly MessageRateLimiter _limiter;
    private readonly Lock _gate = new();
    private List<string> _received = [];
    private PlaySession _session;
    private bool _open = true;

    public LocalConnection(PlaySessionFactory factory, PlaySessionHub hub, int messagesPerSecond, bool playedByModule)
    {
        _factory = factory;
        _hub = hub;
        _limiter = new MessageRateLimiter(messagesPerSecond);
        _session = factory.Create();
        hub.Register(_session, Receive, playedByModule);
    }

    public bool IsOpen => _open;

    private Task Receive(string message)
    {
        lock (_gate) _received.Add(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> TakeReceived()
    {
        lock (_gate)
        {
            var taken = _received;
            _received = [];
            return taken;
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_open) throw new InvalidOperationException("The connection is closed.");
        var session = _session;
        if (!_limiter.TryTake(_factory.World.Time.GetUtcNow().UtcTicks))
        {
            await _hub.SendInOrderAsync(session,
                () => Task.FromResult<IReadOnlyList<string>>([session.RefuseRateLimited()]));
            return;
        }

        await _hub.SendInOrderAsync(session, () => session.HandleTextAsync(message, cancellationToken));
        if (session.LeftForCharacterList) _session = await _hub.ReplaceAsync(session, _factory.Create(), Receive);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_open) return;
        _open = false;
        await _hub.EndAsync(_session);
    }
}
