using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Ubiquitous.Server;

internal sealed class GatewayConnection
{
    private static readonly TimeSpan FlushGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(1);

    private readonly WebSocket _socket;
    private readonly CancellationToken _shutdown;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _dropped;
    private readonly ConnectionOutbox _outbox;
    private Task _writer = Task.CompletedTask;

    public GatewayConnection(WebSocket socket, CancellationToken shutdown, ILogger logger)
    {
        _socket = socket;
        _shutdown = shutdown;
        _logger = logger;
        _dropped = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        _outbox = new ConnectionOutbox(WriteAsync);
    }

    public WebSocket Socket => _socket;

    public CancellationToken Token => _dropped.Token;

    private bool Dropped => _dropped.IsCancellationRequested && !_shutdown.IsCancellationRequested;

    public void Start() => _writer = _outbox.RunAsync(_dropped.Token);

    public Task Send(string message)
    {
        if (!_outbox.TryEnqueue(message)) Drop();
        return Task.CompletedTask;
    }

    public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseOutputAsync(status, description, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task FinishAsync()
    {
        _outbox.Complete();
        if (Dropped)
        {
            await Settled(_writer);
            await CloseBriefly(WebSocketCloseStatus.PolicyViolation, "Too many unread messages");
            return;
        }

        if (await Task.WhenAny(_writer, Task.Delay(FlushGrace)) != _writer) Drop();
        await Settled(_writer);
        await CloseBriefly(WebSocketCloseStatus.NormalClosure, "Server shutting down");
    }

    private void Drop()
    {
        try
        {
            if (!_dropped.IsCancellationRequested) _logger.LogWarning("Closing a connection that is not reading its messages");
            _dropped.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WriteAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task CloseBriefly(WebSocketCloseStatus status, string description)
    {
        using var grace = new CancellationTokenSource(CloseGrace);
        try
        {
            await CloseAsync(status, description, grace.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            _socket.Abort();
        }
    }

    private static async Task Settled(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }
}
