using System.Threading.Channels;

namespace Ubiquitous.Server;

public sealed class ConnectionOutbox
{
    public const int DefaultCapacity = 1024;

    private readonly Channel<string> _queue;
    private readonly Func<string, CancellationToken, Task> _write;

    public ConnectionOutbox(Func<string, CancellationToken, Task> write, int capacity = DefaultCapacity)
    {
        _write = write;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public bool TryEnqueue(string message) => _queue.Writer.TryWrite(message);

    public void Complete() => _queue.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken))
            await _write(message, cancellationToken);
    }
}
