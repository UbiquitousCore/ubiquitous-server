using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using Ubiquitous.World;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly InstanceService _instances;

    private readonly ScriptRegistry _scripts;

    private string? _instanceKey;

    private InstanceWorldState? _instanceWorld;

    private DungeonCopy _copy = DungeonCopy.Normal;

    public string? CurrentInstanceKey => _instanceKey;

    public bool IsReturningHome(string wireId) => _shared?.IsReturning(wireId) == true;


    private bool IsDungeonMap(ushort mapId) => _scripts.IsDungeonMap(mapId);

    private InstanceEntryResult ResolveInstance(ushort mapId) =>
        _instances.Enter(PlayerUnitId, mapId, InstanceOwnerId, () => SharedForInstance(mapId));

    private string InstanceOwnerId =>
        _partyHandler.MyRoster?.Members.FirstOrDefault(m => m.IsLeader)?.Id ?? PlayerUnitId;

    private static SharedMapState SharedForInstance(ushort mapId) => new(mapId);

    private IReadOnlyList<string> DungeonRestart()
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before resetting a dungeon.")];

        var mine = _instances.KeysOwnedBy(PlayerUnitId);
        if (!_instances.ResetOwnedBy(PlayerUnitId))
            return [Error(WireCodes.ProtocolError.DungeonResetRefused, "You cannot reset a dungeon while anyone is still inside it.")];

        return [Reply(WebMessageTypes.DungeonRestarted, new DungeonRestartedPayload(mine.Count))];
    }

    private IReadOnlyList<string> DungeonChooseMode(JsonElement payload)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before choosing a dungeon mode.")];

        var request = WebMessageJson.DeserializePayload<DungeonChooseModePayload>(payload);
        if (ModeFromWire(request.Mode) is not { } mode)
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"Unknown dungeon mode '{request.Mode}'.")];
        if (!string.Equals(InstanceOwnerId, PlayerUnitId, StringComparison.Ordinal))
            return [Error(WireCodes.ProtocolError.DungeonModeRefused, "Only the team leader can change the dungeon mode.")];

        var ending = _instances.KeysOwnedBy(PlayerUnitId);
        return _instances.ChooseMode(PlayerUnitId, mode) switch
        {
            DungeonModeChange.Refused =>
                [Error(WireCodes.ProtocolError.DungeonModeRefused, "You cannot change the dungeon mode while anyone is still inside a dungeon.")],
            DungeonModeChange.Unchanged => [Reply(WebMessageTypes.DungeonModeChosen, new DungeonModeChosenPayload(request.Mode, 0))],
            _ => [Reply(WebMessageTypes.DungeonModeChosen, new DungeonModeChosenPayload(request.Mode, ending.Count))],
        };
    }

    private static DungeonMode? ModeFromWire(string? mode) => mode switch
    {
        WireCodes.DungeonMode.Normal => DungeonMode.Normal,
        WireCodes.DungeonMode.Legend => DungeonMode.Legend,
        _ => null,
    };

    private async Task<IReadOnlyList<string>> InstanceEnter(JsonElement payload)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before entering an instance.")];

        var request = WebMessageJson.DeserializePayload<InstanceEnterPayload>(payload);
        if (request.MapId is < 0 or > ushort.MaxValue)
            return [Error(WireCodes.ProtocolError.InvalidInstance, "Map id is out of range.")];
        var mapId = (ushort)request.MapId;
        if (_content.Maps.Count > 0 && !_content.Maps.ContainsKey(mapId))
            return [Error(WireCodes.ProtocolError.InvalidInstance, $"Unknown map {mapId}.")];

        var result = ResolveInstance(mapId);

        var pos = request.Position is { } wire
            ? new DomainPosition((float)wire.X, (float)wire.Y, (float)wire.Z)
            : new DomainPosition(0, 0, 0);

        return EnterWorld(mapId, pos, request.Yaw, result.InstanceKey, result.World, result.Shared, result.Mode);
    }

    public void LeaveInstanceIfAny()
    {
        if (_instanceKey is not null) _instances.Leave(PlayerUnitId, _instanceKey);
    }
}
