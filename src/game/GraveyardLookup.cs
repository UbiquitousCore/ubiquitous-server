using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class GraveyardLookup
{
    public readonly record struct GraveyardDestination(ushort MapId, Position Position, float Yaw);

    public static GraveyardDestination Resolve(GameContent content, ushort mapId, Position from) =>
        NearestOn(content, mapId, from)
        ?? RecallPointOf(content, mapId)
        ?? new GraveyardDestination(mapId, from, Yaw: 0f);

    private static GraveyardDestination? RecallPointOf(GameContent content, ushort mapId) =>
        content.Maps.TryGetValue(mapId, out var map) && map.RecallMapId is { } recallMapId
            ? new GraveyardDestination(recallMapId, new Position(map.RecallX ?? 0f, map.RecallY ?? 0f, map.RecallZ ?? 0f), Yaw: 0f)
            : null;

    private static GraveyardDestination? NearestOn(GameContent content, ushort mapId, Position from)
    {
        MapGraveyardDefinition? nearest = null;
        float nearestDistanceSq = float.MaxValue;
        foreach (var graveyard in content.Graveyards)
        {
            if (graveyard.MapId != mapId) continue;

            float dx = graveyard.X - from.X;
            float dz = graveyard.Z - from.Z;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq < nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                nearest = graveyard;
            }
        }

        return nearest is { } found
            ? new GraveyardDestination(found.MapId, new Position(found.X, found.Y, found.Z), found.Yaw)
            : null;
    }
}
