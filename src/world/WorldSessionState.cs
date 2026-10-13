using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.World;

public sealed record WorldEntitySnapshot(
    EntityId Id,
    uint Entry,
    Position Position,
    float Yaw,
    uint SpawnId);

public sealed record MovementDecision(
    bool Accepted,
    Position Position,
    float Yaw,
    bool Corrected,
    float DistanceSquared);

public sealed class WorldSessionState
{
    public const float DefaultViewRange = 60f;
    private const float MaxStepDistanceSquared = PlayerMovementLimits.MaxStepDistance * PlayerMovementLimits.MaxStepDistance;

    private readonly (float MinX, float MaxX, float MinZ, float MaxZ)? _bounds;
    private Position _playerPosition;
    private float _playerYaw;

    public WorldSessionState(ushort mapId, Position playerPosition, float playerYaw, MapDefinition? map = null)
    {
        MapId = mapId;
        _bounds = map is { MinX: { } minX, MaxX: { } maxX, MinZ: { } minZ, MaxZ: { } maxZ }
            ? (minX, maxX, minZ, maxZ)
            : null;
        _playerPosition = playerPosition;
        _playerYaw = playerYaw;
    }

    public ushort MapId { get; }
    public Position PlayerPosition => _playerPosition;
    public float PlayerYaw => _playerYaw;

    public MovementDecision ApplyPlayerMovement(Position requested, float yaw, float speedMultiplier = 1f, bool bypassValidation = false)
    {
        var distanceSquared = _playerPosition.DistanceSquaredTo(requested);
        var cap = MaxStepDistanceSquared * speedMultiplier * speedMultiplier;
        if (!bypassValidation && (distanceSquared > cap || !IsValidPosition(requested)))
        {
            return new MovementDecision(false, _playerPosition, _playerYaw, true, distanceSquared);
        }

        _playerPosition = requested;
        _playerYaw = yaw;
        return new MovementDecision(true, requested, yaw, false, distanceSquared);
    }

    private bool IsValidPosition(Position position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Z)) return false;
        if (_bounds is not { } bounds) return true;
        return position.X >= bounds.MinX && position.X <= bounds.MaxX
            && position.Z >= bounds.MinZ && position.Z <= bounds.MaxZ;
    }
}
