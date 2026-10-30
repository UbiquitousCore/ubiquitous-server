using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed class PlaySessionHub(ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    private readonly ConcurrentDictionary<PlaySession, Func<string, Task>> _sessions = new();

    private readonly ConcurrentDictionary<PlaySession, SemaphoreSlim> _outbound = new();
    private readonly ConcurrentDictionary<PlaySession, Channel<OutboundEvent>> _inbox = new();
    // Where each session was last announced. An arrival counts as a move even
    // on the same map, so a teleport is shown to others as a fresh spawn.
    private readonly ConcurrentDictionary<PlaySession, (ushort MapId, string? InstanceKey, int Arrival)?> _presentLocation = new();

    private readonly ConcurrentDictionary<PlaySession, long> _joined = new();
    private long _joinCount;

    private readonly ConcurrentDictionary<PlaySession, HashSet<PlaySession>> _hiddenFrom = new();

    private readonly ConcurrentDictionary<PlaySession, byte> _playedByModules = new();

    public void Register(PlaySession session, Func<string, Task> send, bool playedByModule = false)
    {
        if (playedByModule) _playedByModules[session] = 0;
        _sessions[session] = send;
        _outbound[session] = new SemaphoreSlim(1, 1);
        _inbox[session] = Channel.CreateUnbounded<OutboundEvent>(new UnboundedChannelOptions { SingleReader = false });
        _presentLocation[session] = null;
        _joined[session] = Interlocked.Increment(ref _joinCount);
        session.AttachHub(this);
    }

    public PlaySession? FindByCharacterNetworkId(WebNetworkId networkId) =>
        _sessions.Keys
            .Where(session => session.Presence?.NetworkId == networkId)
            .OrderByDescending(session => _joined.TryGetValue(session, out var at) ? at : 0)
            .FirstOrDefault();

    public PlaySession? FindByCharacterName(string name) =>
        _sessions.Keys
            .Where(session => string.Equals(session.Presence?.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(session => _joined.TryGetValue(session, out var at) ? at : 0)
            .FirstOrDefault();

    public IReadOnlyList<PlayerPresence> OnlinePlayers() =>
        [.. _sessions.Keys
            .Select(session => session.Presence)
            .OfType<PlayerPresence>()
            .DistinctBy(presence => presence.NetworkId)
            .OrderBy(presence => presence.Name, StringComparer.OrdinalIgnoreCase)];

    public async Task DeliverAsync(PlaySession target, string rawMessage)
    {
        if (OutboundEvent.From(rawMessage) is { } outbound) await DeliverAsync(target, outbound);
    }

    private async Task DeliverAsync(PlaySession target, OutboundEvent outbound)
    {
        if (!_inbox.TryGetValue(target, out var inbox)) return;
        inbox.Writer.TryWrite(outbound);
        await DrainIfIdleAsync(target);
    }

    private async Task DrainIfIdleAsync(PlaySession session)
    {
        while (_inbox.TryGetValue(session, out var inbox) && inbox.Reader.TryPeek(out _)
            && _sessions.TryGetValue(session, out var send)
            && _outbound.TryGetValue(session, out var gate)
            && await gate.WaitAsync(0))
        {
            try
            {
                await DrainInboxAsync(session, send);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task DrainInboxAsync(PlaySession session, Func<string, Task> send)
    {
        if (!_inbox.TryGetValue(session, out var inbox)) return;
        while (inbox.Reader.TryRead(out var outbound))
            await send(session.EncodeObserverEvent(outbound));
    }

    private void Remove(PlaySession session)
    {
        _sessions.TryRemove(session, out _);
        _playedByModules.TryRemove(session, out _);
        _presentLocation.TryRemove(session, out _);
        _joined.TryRemove(session, out _);
        ForgetSight(session);
        _outbound.TryRemove(session, out _);
        if (_inbox.TryRemove(session, out var inbox)) inbox.Writer.TryComplete();
    }

    private async Task LeaveAsync(PlaySession session)
    {
        if (_presentLocation.TryGetValue(session, out var location) && location is { } left)
        {
            await AnnounceLeaveAsync(session, left.MapId, left.InstanceKey);
        }
    }

    public async Task EndAsync(PlaySession session)
    {
        try
        {
            await session.SaveProgressAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disconnect save failed");
        }

        session.LeaveWorld();
        await DisconnectAsync(session);
    }

    // Back to the character list: the account carries over to a fresh session on the same connection.
    public async Task<PlaySession> ReplaceAsync(PlaySession left, PlaySession fresh, Func<string, Task> send)
    {
        await EndAsync(left);
        fresh.CarryOnFrom(left);
        Register(fresh, send, _playedByModules.TryRemove(left, out _));
        return fresh;
    }

    public async Task DisconnectAsync(PlaySession session)
    {
        await LeaveAsync(session);
        Remove(session);
        await session.GoOfflineInPartyAsync();
    }

    public bool IsPlayerNear(ushort mapId, Ubiquitous.Common.Position at, float within)
    {
        foreach (var session in _sessions.Keys)
        {
            if (_playedByModules.ContainsKey(session) || session.CurrentMapId != mapId) continue;
            if (session.PlayerPosition is { } standing && standing.DistanceSquaredTo(at) <= within * within) return true;
        }
        return false;
    }

    private static bool SameLocation(PlaySession a, PlaySession b) =>
        a.CurrentMapId == b.CurrentMapId && a.CurrentInstanceKey == b.CurrentInstanceKey;

    public async Task TickAsync(Func<PlaySession, IReadOnlyList<string>> tick)
    {
        foreach (var pair in _sessions)
        {
            try
            {
                await SendInOrderAsync(pair.Key, () => Task.FromResult(tick(pair.Key)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session tick failed");
            }
        }
    }

    public async Task AutosaveAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        foreach (var pair in _sessions)
        {
            if (!pair.Key.IsAutosaveDue(now)) continue;
            try
            {
                await SendInOrderAsync(pair.Key, async () =>
                {
                    await pair.Key.AutosaveAsync(now, cancellationToken);
                    return [];
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Session autosave failed");
            }
        }
    }

    public async Task SendInOrderAsync(PlaySession session, Func<Task<IReadOnlyList<string>>> produce)
    {
        if (!_sessions.TryGetValue(session, out var send) || !_outbound.TryGetValue(session, out var gate)) return;
        await gate.WaitAsync();
        try
        {
            await DrainInboxAsync(session, send);
            var responses = await produce();
            foreach (var response in responses) await send(response);
            var presence = await BroadcastAsync(session, responses);
            foreach (var message in presence) await send(message);
            await DrainInboxAsync(session, send);
        }
        finally
        {
            gate.Release();
        }

        await DrainIfIdleAsync(session);
    }

    public async Task<IReadOnlyList<string>> BroadcastAsync(PlaySession sender, IReadOnlyList<string> responses)
    {
        await ReconcileSightAsync(sender);

        var events = responses.Select(sender.DescribeReply).OfType<OutboundEvent>().ToArray();
        var worldEvents = events.Where(EventAudience.IsMapWide).ToArray();
        if (worldEvents.Length > 0)
        {
            foreach (var pair in _sessions)
            {
                if (ReferenceEquals(pair.Key, sender) || !SameLocation(pair.Key, sender)) continue;
                foreach (var outbound in worldEvents)
                {
                    if (IsAboutHiddenBody(pair.Key, outbound)) continue;
                    await DeliverAsync(pair.Key, outbound);
                }
            }
        }

        foreach (var chat in events.Where(IsChatMessage))
        {
            var channel = chat.ChatChannel!;
            var senderGuid = sender.CharacterGuid;
            if (ChatService.IsPartyChannel(channel))
            {
                foreach (var memberId in sender.PartyMemberIdsExcludingSelf())
                {
                    var target = FindByCharacterNetworkId(WebNetworkId.FromString(memberId));
                    if (target is not null && (senderGuid is null || !await target.IgnoresAsync(senderGuid)))
                        await DeliverAsync(target, chat);
                }
                continue;
            }

            if (ChatService.IsGuildChannel(channel))
            {
                foreach (var memberId in await sender.GuildMemberIdsExcludingSelfAsync())
                {
                    var target = FindByCharacterNetworkId(WebNetworkId.FromString(memberId));
                    if (target is not null && (senderGuid is null || !await target.IgnoresAsync(senderGuid)))
                        await DeliverAsync(target, chat);
                }
                continue;
            }

            var serverWide = ChatService.IsServerWide(channel);
            foreach (var pair in _sessions)
            {
                if (ReferenceEquals(pair.Key, sender)) continue;
                if (serverWide ? pair.Key.CurrentMapId is null : !SameLocation(pair.Key, sender)) continue;
                if (!CanDeliverChat(channel, sender, pair.Key)) continue;
                if (senderGuid is not null && await pair.Key.IgnoresAsync(senderGuid)) continue;
                await DeliverAsync(pair.Key, chat);
            }
        }

        return await SyncPresenceAsync(sender);
    }

    private static bool IsChatMessage(OutboundEvent outbound) =>
        outbound is { Type: WebMessageTypes.ChatReceived, ChatChannel: not null };

    private static bool CanDeliverChat(string channel, PlaySession sender, PlaySession observer)
    {
        if (ChatService.RangeOf(channel) is { } range)
        {
            var from = sender.PlayerPosition;
            var to = observer.PlayerPosition;
            return from is not null && to is not null
                && from.Value.DistanceSquaredTo(to.Value) <= range * range;
        }

        return observer.IsChatChannelJoined(channel);
    }

    private async Task<IReadOnlyList<string>> SyncPresenceAsync(PlaySession session)
    {
        var currentLocation = session.CurrentMapId is { } map
            ? (map, session.CurrentInstanceKey, session.Arrivals)
            : ((ushort MapId, string? InstanceKey, int Arrival)?)null;
        var previousLocation = _presentLocation.TryGetValue(session, out var known) ? known : null;
        if (currentLocation == previousLocation) return [];
        _presentLocation[session] = currentLocation;

        if (previousLocation is { } left) await AnnounceLeaveAsync(session, left.MapId, left.InstanceKey);

        var toSelf = new List<string>();
        var presence = session.Presence;
        if (currentLocation is null || presence is null) return toSelf;

        var spawned = SpawnEventFor(presence);
        foreach (var pair in _sessions)
        {
            if (ReferenceEquals(pair.Key, session) || !SameLocation(pair.Key, session)) continue;

            if (!NoteSight(session, pair.Key))
            {
                foreach (var shown in ShownEventsFor(session, presence, spawned)) await DeliverAsync(pair.Key, shown);
            }

            if (NoteSight(pair.Key, session)) continue;
            var otherPresence = pair.Key.Presence;
            if (otherPresence is null) continue;
            foreach (var shown in ShownEventsFor(pair.Key, otherPresence, SpawnEventFor(otherPresence)))
                toSelf.Add(session.EncodeObserverEvent(shown));
        }

        return toSelf;
    }

    private async Task AnnounceLeaveAsync(PlaySession session, ushort mapId, string? instanceKey)
    {
        ForgetSight(session);
        var networkId = session.Presence?.NetworkId;
        if (networkId is null) return;
        var removed = OutboundEvent.Of(WebMessageTypes.EntityRemoved, new EntityRemovedPayload(networkId.Value));

        foreach (var pair in _sessions)
        {
            if (ReferenceEquals(pair.Key, session) || pair.Key.CurrentMapId != mapId || pair.Key.CurrentInstanceKey != instanceKey) continue;
            await DeliverAsync(pair.Key, removed);
        }
    }

    private async Task ReconcileSightAsync(PlaySession subject)
    {
        var hidden = _hiddenFrom.GetOrAdd(subject, static _ => new HashSet<PlaySession>());
        bool anyHidden;
        lock (hidden) anyHidden = hidden.Count > 0;
        if (!anyHidden && !subject.IsStealthed) return;
        if (!IsAnnouncedWhereItStands(subject)) return;

        PlayerPresence? presence = null;
        foreach (var pair in _sessions)
        {
            var observer = pair.Key;
            if (ReferenceEquals(observer, subject)) continue;
            bool wasHidden;
            lock (hidden) wasHidden = hidden.Contains(observer);
            if (!SameLocation(observer, subject) || !IsAnnouncedWhereItStands(observer))
            {
                if (wasHidden) lock (hidden) hidden.Remove(observer);
                continue;
            }

            bool hide = subject.HidesFrom(observer);
            if (hide == wasHidden) continue;
            lock (hidden)
            {
                if (hide) hidden.Add(observer);
                else hidden.Remove(observer);
            }

            IEnumerable<OutboundEvent> events;
            if (hide)
            {
                var networkId = subject.Presence?.NetworkId;
                if (networkId is null) continue;
                events = [OutboundEvent.Of(WebMessageTypes.EntityRemoved, new EntityRemovedPayload(networkId.Value))];
            }
            else
            {
                presence ??= subject.Presence;
                if (presence is null) continue;
                events = ShownEventsFor(subject, presence, SpawnEventFor(presence));
            }
            foreach (var outbound in events) await DeliverAsync(observer, outbound);
        }
    }

    private bool NoteSight(PlaySession subject, PlaySession observer)
    {
        var hide = subject.HidesFrom(observer);
        var hidden = _hiddenFrom.GetOrAdd(subject, static _ => new HashSet<PlaySession>());
        lock (hidden)
        {
            if (hide) hidden.Add(observer);
            else hidden.Remove(observer);
        }
        return hide;
    }

    private void ForgetSight(PlaySession session)
    {
        if (_hiddenFrom.TryRemove(session, out var own)) lock (own) own.Clear();
        foreach (var pair in _hiddenFrom)
            lock (pair.Value) pair.Value.Remove(session);
    }

    private bool IsAboutHiddenBody(PlaySession observer, OutboundEvent outbound)
    {
        foreach (var pair in _hiddenFrom)
        {
            bool hidden;
            lock (pair.Value) hidden = pair.Value.Contains(observer);
            if (!hidden || pair.Key.PlayerNetworkIdText is not { } id) continue;
            if (outbound.MentionedPlayerIds.Contains(id, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    private bool IsAnnouncedWhereItStands(PlaySession session) =>
        session.CurrentMapId is { } map
        && _presentLocation.TryGetValue(session, out var known)
        && known == (map, session.CurrentInstanceKey, session.Arrivals);

    private static IEnumerable<OutboundEvent> ShownEventsFor(PlaySession subject, PlayerPresence presence, OutboundEvent spawned)
    {
        yield return spawned;
        if (subject.RiddenMountId is uint mount)
            yield return OutboundEvent.Of(WebMessageTypes.EntityValuesChanged,
                new EntityValuesChangedPayload(presence.NetworkId, new Dictionary<string, int> { [WireCodes.UnitValue.Mount] = (int)mount }));
        if (subject.IsStealthed)
            yield return OutboundEvent.Of(WebMessageTypes.EntityValuesChanged,
                new EntityValuesChangedPayload(presence.NetworkId, new Dictionary<string, int> { [WireCodes.UnitValue.Stealthed] = 1 }));
    }

    private static OutboundEvent SpawnEventFor(PlayerPresence presence) =>
        OutboundEvent.Of(WebMessageTypes.EntitySpawned,
            new PlayerSpawnedPayload(presence.NetworkId, presence.Position, presence.Yaw,
                presence.Name, presence.Level, presence.Class, presence.Race, presence.Gender, presence.DisplayInfoId,
                presence.Skin, presence.HairStyle, presence.HairColour, presence.Face, presence.Equipment,
                presence.Health, presence.MaxHealth, presence.Power, presence.MaxPower, presence.Auras));
}
