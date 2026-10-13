using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ConnectionOutboxTests
{
    [Fact]
    public async Task AWriterThatNeverFinishes_FillsTheQueueAndRefusesTheNextMessage()
    {
        const int capacity = 4;
        var stuck = new TaskCompletionSource();
        var writing = new TaskCompletionSource();
        var outbox = new ConnectionOutbox((_, _) => { writing.TrySetResult(); return stuck.Task; }, capacity);
        using var cancel = new CancellationTokenSource();
        var running = outbox.RunAsync(cancel.Token);
        Assert.True(outbox.TryEnqueue("taken by the writer"));
        await writing.Task;

        for (var message = 0; message < capacity; message++) Assert.True(outbox.TryEnqueue($"m{message}"));

        Assert.False(outbox.TryEnqueue("one more"));
        cancel.Cancel();
        stuck.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task MessagesAreWrittenInTheOrderTheyWereQueued()
    {
        var written = new List<string>();
        var outbox = new ConnectionOutbox((message, _) => { written.Add(message); return Task.CompletedTask; });
        var running = outbox.RunAsync(CancellationToken.None);

        for (var message = 0; message < 50; message++) outbox.TryEnqueue($"m{message}");
        outbox.Complete();
        await running;

        Assert.Equal(Enumerable.Range(0, 50).Select(message => $"m{message}"), written);
    }
}

public sealed class MessageRateLimiterTests
{
    [Fact]
    public void ABurstUpToTheRateIsAllowedAndTheNextMessageIsNot()
    {
        var limiter = new MessageRateLimiter(3);

        Assert.True(limiter.TryTake(0));
        Assert.True(limiter.TryTake(0));
        Assert.True(limiter.TryTake(0));
        Assert.False(limiter.TryTake(0));
    }

    [Fact]
    public void MessagesAreAllowedAgainAsTimePasses()
    {
        var limiter = new MessageRateLimiter(2);
        limiter.TryTake(0);
        limiter.TryTake(0);
        Assert.False(limiter.TryTake(0));

        Assert.True(limiter.TryTake(TimeSpan.TicksPerSecond / 2));
        Assert.False(limiter.TryTake(TimeSpan.TicksPerSecond / 2));
    }
}
