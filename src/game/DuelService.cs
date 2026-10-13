using System.Collections.Concurrent;
using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum DuelEndReason
{
    Lost,
    Won,
    Forfeited,
    OpponentForfeited,
}

public abstract record DuelNotification(string OpponentId);

public sealed record DuelChallenged(string OpponentId) : DuelNotification(OpponentId);

public sealed record DuelDeclined(string OpponentId) : DuelNotification(OpponentId);

public sealed record DuelStarted(string OpponentId) : DuelNotification(OpponentId);

public sealed record DuelOver(string OpponentId, DuelEndReason Reason) : DuelNotification(OpponentId);

public sealed class DuelService
{
    public const double AcceptRange = 15.0;

    public static readonly TimeSpan RequestExpiry = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _countdown;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _startsAt = new(StringComparer.Ordinal);

    private readonly TimeProvider _time;

    public DuelService(TimeSpan? countdown = null, TimeProvider? time = null)
    {
        _countdown = countdown ?? Countdown;
        _time = time ?? TimeProvider.System;
    }

    public DateTimeOffset? StartsAt(string playerId) => _startsAt.TryGetValue(playerId, out var at) ? at : null;

    private sealed record PendingRequest(string FromId, Position FromPosition, DateTimeOffset RequestedAt);

    private readonly ConcurrentDictionary<string, PendingRequest> _pendingByTarget = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _activeOpponent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DuelNotification>> _mailbox = new(StringComparer.Ordinal);

    public enum RequestOutcome { Ok, SelfDuel, AlreadyDuelling }

    public RequestOutcome Request(string fromId, Position fromPosition, string toId, DateTimeOffset now)
    {
        if (string.Equals(fromId, toId, StringComparison.Ordinal)) return RequestOutcome.SelfDuel;
        if (_activeOpponent.ContainsKey(fromId) || _activeOpponent.ContainsKey(toId)) return RequestOutcome.AlreadyDuelling;

        _pendingByTarget[toId] = new PendingRequest(fromId, fromPosition, now);
        Enqueue(toId, new DuelChallenged(fromId));
        return RequestOutcome.Ok;
    }

    public enum AcceptOutcome { Ok, NoPendingRequest, AlreadyDuelling, OutOfRange, Expired }

    public sealed record AcceptResult(AcceptOutcome Outcome, string? OpponentId);

    public AcceptResult Accept(string accepterId, Position accepterPosition, DateTimeOffset now)
    {
        if (!_pendingByTarget.TryRemove(accepterId, out var pending))
            return new AcceptResult(AcceptOutcome.NoPendingRequest, null);

        if (now - pending.RequestedAt > RequestExpiry)
        {
            Enqueue(pending.FromId, new DuelDeclined(accepterId));
            return new AcceptResult(AcceptOutcome.Expired, null);
        }

        if (_activeOpponent.ContainsKey(accepterId) || _activeOpponent.ContainsKey(pending.FromId))
            return new AcceptResult(AcceptOutcome.AlreadyDuelling, null);

        var distance = Distance(pending.FromPosition, accepterPosition);
        if (distance > AcceptRange)
            return new AcceptResult(AcceptOutcome.OutOfRange, pending.FromId);

        _activeOpponent[accepterId] = pending.FromId;
        _activeOpponent[pending.FromId] = accepterId;
        _startsAt[accepterId] = now + _countdown;
        _startsAt[pending.FromId] = now + _countdown;
        Enqueue(pending.FromId, new DuelStarted(accepterId));
        return new AcceptResult(AcceptOutcome.Ok, pending.FromId);
    }

    public string? Decline(string declinerId)
    {
        if (!_pendingByTarget.TryRemove(declinerId, out var pending)) return null;
        Enqueue(pending.FromId, new DuelDeclined(declinerId));
        return pending.FromId;
    }

    public bool IsActiveOpponent(string playerId, string otherId, DateTimeOffset? now = null) =>
        _activeOpponent.TryGetValue(playerId, out var opponent)
        && string.Equals(opponent, otherId, StringComparison.Ordinal)
        && (!_startsAt.TryGetValue(playerId, out var at) || (now ?? _time.GetUtcNow()) >= at);

    public string? OpponentOf(string playerId) => _activeOpponent.TryGetValue(playerId, out var opponent) ? opponent : null;

    public string? End(string playerId, DuelEndReason reason)
    {
        if (!_activeOpponent.TryRemove(playerId, out var opponentId)) return null;
        _activeOpponent.TryRemove(opponentId, out _);
        _startsAt.TryRemove(playerId, out _);
        _startsAt.TryRemove(opponentId, out _);

        var opponentReason = reason switch
        {
            DuelEndReason.Lost => DuelEndReason.Won,
            DuelEndReason.Won => DuelEndReason.Lost,
            DuelEndReason.Forfeited => DuelEndReason.OpponentForfeited,
            DuelEndReason.OpponentForfeited => DuelEndReason.Forfeited,
            _ => DuelEndReason.Won,
        };
        Enqueue(opponentId, new DuelOver(playerId, opponentReason));
        return opponentId;
    }

    public void Leave(string playerId)
    {
        End(playerId, DuelEndReason.Forfeited);
        _pendingByTarget.TryRemove(playerId, out _);
        foreach (var (targetId, pending) in _pendingByTarget)
        {
            if (string.Equals(pending.FromId, playerId, StringComparison.Ordinal))
                _pendingByTarget.TryRemove(targetId, out _);
        }
        _mailbox.TryRemove(playerId, out _);
    }

    public IReadOnlyList<DuelNotification> Drain(string playerId)
    {
        if (!_mailbox.TryGetValue(playerId, out var queue)) return [];
        var drained = new List<DuelNotification>();
        while (queue.TryDequeue(out var notification)) drained.Add(notification);
        return drained;
    }

    private void Enqueue(string playerId, DuelNotification notification) =>
        _mailbox.GetOrAdd(playerId, static _ => new ConcurrentQueue<DuelNotification>()).Enqueue(notification);

    private static double Distance(Position a, Position b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
