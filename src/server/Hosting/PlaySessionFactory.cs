using Microsoft.Extensions.Logging;

namespace Ubiquitous.Server;

public sealed class PlaySessionFactory(
    SessionStores stores,
    ServerContent content,
    SharedWorld world,
    bool developmentEntry = false,
    ILogger? sessionLogger = null,
    IEnumerable<string>? developmentUserIds = null)
{
    private readonly PlaySessionOptions _options = new(
        developmentEntry, DevelopmentUserIds: (developmentUserIds ?? []).ToHashSet(StringComparer.Ordinal));

    public SharedWorld World { get; } = world;

    public ServerContent Content => content;

    public SessionStores Stores => stores;

    public PlaySession Create() => new(stores, content, World, _options, sessionLogger);
}
