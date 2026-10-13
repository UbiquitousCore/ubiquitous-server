using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

internal sealed record SummonOffer(
    string SummonerId, string SummonerName, ushort MapId, DomainPosition Position, float Yaw, DateTimeOffset Expires);

internal sealed class SummonHandler(ISessionPlayer player, SessionOutbox outbox, GameContent content, PartyService party)
{
    // Own choice: long enough to read the question and answer it, short
    // enough that a forgotten one cannot pull a player across the world later.
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(2);

    private SummonOffer? _offer;

    public IReadOnlyList<string> Open(JsonElement payload)
    {
        if (player.Character is null || player.World is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before summoning.")];
        var stoneId = PayloadIds.NetworkId(payload);
        if (stoneId is null || !player.InReachOfStaticObject(stoneId, content.SummonStoneEntries))
            return [outbox.Error(WireCodes.ProtocolError.InvalidSummon, "That is not a summon stone within reach.")];

        return [outbox.Reply(WebMessageTypes.SummonOpened,
            new SummonOpenedPayload(WebNetworkId.FromString(stoneId), Members()))];
    }

    public async Task<IReadOnlyList<string>> Request(JsonElement payload)
    {
        if (player.Character is null || player.World is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before summoning.")];
        var request = WebMessageJson.DeserializePayload<SummonRequestPayload>(payload);
        if (request.NetworkId.Text is not { } stoneId
            || !player.InReachOfStaticObject(stoneId, content.SummonStoneEntries))
            return [outbox.Error(WireCodes.ProtocolError.InvalidSummon, "That is not a summon stone within reach.")];

        var member = party.RosterOf(player.UnitId)?.Members
            .FirstOrDefault(one => one.Id == request.MemberId.Text && one.Id != player.UnitId);
        if (member is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidSummon, "Only another member of your party can be summoned.")];

        var target = player.Hub?.FindByCharacterNetworkId(request.MemberId);
        if (target?.Presence is null)
            return [Result(member, WireCodes.SummonResult.Failed)];

        var world = player.World;
        var required = SummonRules.RequiredLevel(content.Maps.Values, world.MapId, world.PlayerPosition);
        if (!SummonRules.CanBeSummoned(target.Presence.Level, required))
        {
            await player.Hub!.DeliverAsync(target, SessionOutbox.Unsequenced(WebMessageTypes.SummonTooLow,
                new SummonTooLowPayload(player.Character.Name)));
            return [Result(member, WireCodes.SummonResult.LevelTooLow)];
        }

        target.ReceiveSummon(new SummonOffer(player.UnitId, player.Character.Name, world.MapId,
            world.PlayerPosition, world.PlayerYaw, player.Now + OfferLifetime));
        var at = world.PlayerPosition;
        await player.Hub!.DeliverAsync(target, SessionOutbox.Unsequenced(WebMessageTypes.SummonAsked,
            new SummonAskedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name, world.MapId,
                new Position(at.X, at.Y, at.Z))));
        return [Result(member, WireCodes.SummonResult.Asked)];
    }

    public void Receive(SummonOffer offer) => _offer = offer;

    public async Task<(SummonOffer? Accepted, IReadOnlyList<string> Responses)> Answer(JsonElement payload)
    {
        if (player.Character is null)
            return (null, [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before answering a summon.")]);
        var answer = WebMessageJson.DeserializePayload<SummonAnswerPayload>(payload);
        var offer = _offer;
        _offer = null;
        if (offer is null || offer.Expires < player.Now)
            return (null, [outbox.Error(WireCodes.ProtocolError.InvalidSummon, "Nobody is summoning you.")]);

        var summoner = player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(offer.SummonerId));
        if (summoner is not null)
        {
            var told = SessionOutbox.Unsequenced(WebMessageTypes.SummonResult, new SummonResultPayload(
                WebNetworkId.FromString(player.UnitId), player.Character.Name,
                answer.Accept ? WireCodes.SummonResult.Accepted : WireCodes.SummonResult.Refused));
            await player.Hub!.DeliverAsync(summoner, told);
        }

        return (answer.Accept ? offer : null, []);
    }

    private IReadOnlyList<SummonMemberPayload> Members()
    {
        var roster = party.RosterOf(player.UnitId);
        if (roster is null) return [];
        return [.. roster.Members
            .Where(member => member.Id != player.UnitId)
            .Select(member => new SummonMemberPayload(WebNetworkId.FromString(member.Id), member.Name,
                member.Online && player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(member.Id))?.Presence is not null))];
    }

    private string Result(PartyMember member, string result) =>
        outbox.Reply(WebMessageTypes.SummonResult,
            new SummonResultPayload(WebNetworkId.FromString(member.Id), member.Name, result));
}
