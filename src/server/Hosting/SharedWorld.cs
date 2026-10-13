using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Game;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public sealed class SharedWorld
{
    private readonly ILogger _logger;

    public SharedWorld(SessionStores? stores = null, ILogger? logger = null, TimeProvider? time = null)
    {
        _logger = logger ?? NullLogger.Instance;
        Time = time ?? TimeProvider.System;
        Maps = new SharedMapRegistry(time: Time);
        Duels = new DuelService(time: Time);
        Instances = new InstanceService(Time);
        long UnixSeconds() => Time.GetUtcNow().ToUnixTimeSeconds();
        Mail = new MailService(stores?.Mail ?? new InMemoryMailStore(), UnixSeconds);
        Auctions = new AuctionService(stores?.Auction ?? new InMemoryAuctionStore(), Mail, UnixSeconds);
        Guilds = new GuildService(stores?.Guild ?? new InMemoryGuildStore());
    }

    public TimeProvider Time { get; }
    public SharedMapRegistry Maps { get; init; }
    public DuelService Duels { get; init; }
    public PvpFlagService PvpFlags { get; init; } = new();
    public PlayerBlowRelay Blows { get; init; } = new();
    public TradeService Trades { get; init; } = new();
    public AmbientService Ambient { get; init; } = new();
    public InstanceService Instances { get; init; }
    public PartyService Parties { get; init; } = new();
    public OnlineCharacters Online { get; } = new();
    public MailService Mail { get; }
    public AuctionService Auctions { get; }
    public GuildService Guilds { get; init; }

    public void Tick(DateTimeOffset now)
    {
        foreach (var map in Maps.Live)
        {
            try { map.TickWorld(now); }
            catch (Exception ex) { _logger.LogError(ex, "Map {MapId} tick failed", map.MapId); }
        }

        foreach (var copy in Instances.LiveWorlds)
        {
            try { copy.TickWorld(now); }
            catch (Exception ex) { _logger.LogError(ex, "Instance copy tick failed"); }
        }
    }
}
