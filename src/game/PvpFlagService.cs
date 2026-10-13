using System.Collections.Concurrent;

namespace Ubiquitous.Game;

public sealed class PvpFlagService
{
    private readonly ConcurrentDictionary<string, byte> _flagged = new(StringComparer.Ordinal);

    public bool SetFlagged(string playerId, bool flagged)
    {
        if (flagged) _flagged[playerId] = 0;
        else _flagged.TryRemove(playerId, out _);
        return flagged;
    }

    public bool IsFlagged(string playerId) => _flagged.ContainsKey(playerId);

    public bool IsFlaggedPair(string playerId, string otherId) =>
        !string.Equals(playerId, otherId, StringComparison.Ordinal) && IsFlagged(playerId) && IsFlagged(otherId);
}
