using System.Collections.Concurrent;

namespace Ubiquitous.Server;

public sealed class OnlineCharacters
{
    private readonly ConcurrentDictionary<string, byte> _guids = new(StringComparer.Ordinal);

    public bool TryClaim(string guid) => _guids.TryAdd(guid, 0);

    public void Release(string guid) => _guids.TryRemove(guid, out _);
}
