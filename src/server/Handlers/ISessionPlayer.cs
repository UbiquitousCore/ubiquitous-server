using Ubiquitous.Persistence;
using Ubiquitous.World;

namespace Ubiquitous.Server;

internal interface ISessionPlayer
{
    CharacterRecord? Character { get; }
    string UnitId { get; }
    WorldSessionState? World { get; }
    PlaySessionHub? Hub { get; }
    bool IsAdmin { get; }
    DateTimeOffset Now { get; }

    bool Sees(PlaySession other);

    bool InReachOfStaticObject(string networkId, IReadOnlySet<uint> entries);

    Task SaveInventoryAsync(CancellationToken cancellationToken = default);
}
