using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private IReadOnlyList<string> Teleport(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before travel.")];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null) return [Error(WireCodes.ProtocolError.InvalidTeleport, "Network ID is required.")];

        var result = _teleports.Request(networkId, _sight.Entries, _content.Npcs);
        return [Reply(WebMessageTypes.NpcTeleportAnswer,
            new NpcTeleportAnswerPayload(result.Ok, WebNetworkId.FromString(networkId)))];
    }

    private IReadOnlyList<string> BusRequest(JsonElement payload)
    {
        var quote = QuoteFrom(payload);
        return quote is null ? []
            : [Reply(WebMessageTypes.BusFare, new BusFarePayload(quote.From, quote.To, quote.Cost))];
    }

    private IReadOnlyList<string> BusConfirm(JsonElement payload)
    {
        var quote = QuoteFrom(payload);
        if (quote is null || _world is null || _character is null) return [];
        if (quote.Cost > _belongings.Currency)
            return [Error(WireCodes.ProtocolError.InsufficientFunds, "You cannot afford this journey.")];

        _belongings.Currency -= quote.Cost;
        var responses = new List<string>
        {
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
        };

        var stop = quote.Destination;
        TravelTo(responses, stop.MapId, new DomainPosition(stop.X, stop.Y, stop.Z), stop.Yaw);
        return responses;
    }

    private IReadOnlyList<string> AdminTeleport(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before travel.")];
        if (_account?.IsAdmin != true)
            return [Error(WireCodes.ProtocolError.Forbidden, "Only an administrator may teleport.")];
        var request = WebMessageJson.DeserializePayload<AdminTeleportPayload>(payload);
        if (!_content.Maps.ContainsKey(request.MapId))
            return [Error(WireCodes.ProtocolError.InvalidTeleport, $"There is no map {request.MapId}.")];

        var y = request.Y ?? _content.Terrain?.HeightAt(request.MapId, request.X, request.Z) ?? 0f;
        var responses = new List<string>();
        TravelTo(responses, request.MapId, new DomainPosition(request.X, y, request.Z), _world.PlayerYaw);
        return responses;
    }

    private void TravelTo(List<string> responses, ushort mapId, DomainPosition position, float yaw)
    {
        if (_character is null) return;
        UnmarkEngaged();
        var stopped = _combat.Disengage(PlayerUnitId);
        if (stopped is not null)
        {
            responses.Add(Reply(WebMessageTypes.AttackHalted,
                new AttackHaltedPayload(WebNetworkId.FromString(stopped))));
            responses.Add(Reply(WebMessageTypes.AttackExitCombat,
                new AttackExitCombatPayload(WebNetworkId.FromString(PlayerUnitId))));
        }

        responses.AddRange(EnterWorld(mapId, position, yaw));
    }

    private TravelQuote? QuoteFrom(JsonElement payload)
    {
        if (_world is null || _character is null) return null;
        if (!payload.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Number
            || !payload.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.Number
            || !from.TryGetUInt32(out var fromId) || !to.TryGetUInt32(out var toId))
            return null;
        var player = _world.PlayerPosition;
        return _travel.Quote(fromId, toId, _world.MapId, player.X, player.Y, player.Z);
    }
}
