namespace Ubiquitous.Game;

internal sealed class ServiceGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }
}
