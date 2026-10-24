using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.World;

namespace Ubiquitous.Server;

public sealed class WebSocketGateway : IAsyncDisposable
{
    private const int ReceiveBufferBytes = 64 * 1024;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(SimulationTiming.InCombatIntervalMs);
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RateLimitNoticeInterval = TimeSpan.FromSeconds(1);

    private readonly HttpListener _listener = new();
    private readonly PlaySessionFactory _sessionFactory;
    private readonly ServerReadinessState _readiness;
    private readonly PlaySessionHub _sessions;
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxConnections;
    private readonly int _messagesPerSecond;
    private readonly IReadOnlyList<string> _allowedOrigins;
    private int _activeConnections;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Task, byte> _connections = new();
    private readonly Func<DateTimeOffset, CancellationToken, Task>? _afterTick;
    private Task? _acceptLoop;
    private Task? _tickLoop;

    public WebSocketGateway(
        ServerOptions options,
        PlaySessionFactory sessionFactory,
        ServerReadinessState? readiness = null,
        TimeSpan? idleTimeout = null,
        ILogger? logger = null,
        PlaySessionHub? hub = null,
        Func<DateTimeOffset, CancellationToken, Task>? afterTick = null)
    {
        _listener.Prefixes.Add(ListenerPrefix(options.BindAddress, options.WebSocketPort));
        _sessionFactory = sessionFactory;
        _readiness = readiness ?? new ServerReadinessState();
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _maxConnections = options.MaxConnections;
        _messagesPerSecond = options.MaxMessagesPerSecond;
        _allowedOrigins = options.AllowedOrigins ?? [];
        _logger = logger ?? NullLogger.Instance;
        _sessions = hub ?? new PlaySessionHub(_logger);
        _afterTick = afterTick;
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
        _tickLoop = TickLoopAsync(_shutdown.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        IgnoreListenerErrors(_listener.Stop);
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        }

        await Task.WhenAny(Task.WhenAll(_connections.Keys), Task.Delay(ShutdownGrace));

        if (_tickLoop is not null)
        {
            try { await _tickLoop; }
            catch (OperationCanceledException) { }
        }

        IgnoreListenerErrors(_listener.Close);
        _shutdown.Dispose();
    }

    // HttpListener on Linux refuses an all-interfaces IP; it wants the `+` wildcard.
    public static string ListenerPrefix(string bindAddress, int port) =>
        $"http://{(bindAddress is "0.0.0.0" or "::" or "[::]" ? "+" : bindAddress)}:{port}/";

    private static void IgnoreListenerErrors(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
    }

    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        var turn = new WorldTick(_sessionFactory.World, _sessions, _afterTick, _logger);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await turn.RunAsync(DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick loop failed");
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException
                && cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var connection = HandleContextAsync(context, cancellationToken);
            _connections.TryAdd(connection, 0);
            _ = connection.ContinueWith(done => _connections.TryRemove(done, out _), TaskScheduler.Default);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.Request.IsWebSocketRequest)
            {
                await AcceptWebSocketAsync(context, cancellationToken);
            }
            else if (context.Request.Url?.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase) == true)
            {
                await WriteHealthAsync(context.Response, cancellationToken);
            }
            else
            {
                Reject(context.Response);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed");
        }
    }

    private async Task WriteHealthAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var readiness = _readiness.Current;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(readiness));
        response.StatusCode = readiness.ProcessUp ? (int)HttpStatusCode.OK : (int)HttpStatusCode.ServiceUnavailable;
        response.ContentType = "application/json";
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static void Reject(HttpListenerResponse response, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        response.StatusCode = (int)status;
        response.Close();
    }

    private async Task AcceptWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!OriginAllowed(context.Request))
        {
            Reject(context.Response, HttpStatusCode.Forbidden);
            return;
        }

        if (Interlocked.Increment(ref _activeConnections) > _maxConnections)
        {
            Interlocked.Decrement(ref _activeConnections);
            Reject(context.Response, HttpStatusCode.ServiceUnavailable);
            return;
        }

        try
        {
            await ServeConnectionAsync(context, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private bool OriginAllowed(HttpListenerRequest request)
    {
        if (_allowedOrigins.Count == 0) return true;
        var origin = request.Headers["Origin"];
        return origin is null || _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private async Task ServeConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocketContext socketContext;
        try
        {
            socketContext = await context.AcceptWebSocketAsync(null);
        }
        catch (HttpListenerException)
        {
            Reject(context.Response);
            return;
        }

        using var socket = socketContext.WebSocket;
        var connection = new GatewayConnection(socket, cancellationToken, _logger);
        connection.Start();
        var session = _sessionFactory.Create();
        _sessions.Register(session, connection.Send);
        try
        {
            while (await ReceiveLoopAsync(connection, session))
                session = await _sessions.ReplaceAsync(session, _sessionFactory.Create(), connection.Send);
        }
        catch (OperationCanceledException) when (connection.Token.IsCancellationRequested)
        {
        }
        finally
        {
            await _sessions.EndAsync(session);
            await connection.FinishAsync();
        }
    }

    private async Task<bool> ReceiveLoopAsync(GatewayConnection connection, PlaySession session)
    {
        var socket = connection.Socket;
        var cancellationToken = connection.Token;
        var buffer = new byte[ReceiveBufferBytes];
        var assembler = new WebSocketMessageAssembler();
        var limiter = new MessageRateLimiter(_messagesPerSecond);
        long? lastRateLimitNotice = null;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var idleTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var receive = socket.ReceiveAsync(buffer, cancellationToken);
            var idle = Task.Delay(_idleTimeout, idleTimer.Token);
            var first = await Task.WhenAny(receive, idle);
            idleTimer.Cancel();
            if (first == idle)
            {
                await connection.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Idle timeout", CancellationToken.None);
                return false;
            }

            var result = await receive;
            if (result.MessageType == WebSocketMessageType.Close) return false;
            if (result.MessageType != WebSocketMessageType.Text)
            {
                await connection.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Binary messages are not supported", cancellationToken);
                return false;
            }

            string? message;
            try
            {
                message = assembler.Append(buffer.AsSpan(0, result.Count), result.EndOfMessage);
            }
            catch (MessageTooLargeException)
            {
                await connection.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Text message exceeds size limit", cancellationToken);
                return false;
            }

            if (message is null) continue;
            var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
            if (!limiter.TryTake(nowTicks))
            {
                if (lastRateLimitNotice is not { } noticedAt || nowTicks - noticedAt >= RateLimitNoticeInterval.Ticks)
                {
                    lastRateLimitNotice = nowTicks;
                    await _sessions.SendInOrderAsync(session,
                        () => Task.FromResult<IReadOnlyList<string>>([session.RefuseRateLimited()]));
                }

                continue;
            }

            await HandleMessageAsync(session, message, cancellationToken);
            if (session.LeftForCharacterList) return true;
        }

        return false;
    }

    private async Task HandleMessageAsync(PlaySession session, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _sessions.SendInOrderAsync(session, () => session.HandleTextAsync(message, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Message handling failed");
        }
    }
}
