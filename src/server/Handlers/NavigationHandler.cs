using System.Text.Json;
using Ubiquitous.Protocol.Web;
using Ubiquitous.World;

namespace Ubiquitous.Server;

internal sealed class NavigationHandler(ISessionPlayer player, SessionOutbox outbox, WalkableGroundSource? walkableGround)
{
    private const float DefaultRadius = 60f;

    private const float MaxRadius = 240f;

    private const int MaxPolygons = 4000;

    public IReadOnlyList<string> Describe(JsonElement payload)
    {
        if (player.World is null || player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before asking for walkable ground.")];

        if (!player.IsAdmin)
            return [outbox.Error(WireCodes.ProtocolError.Forbidden, "Walkable ground is an operator facility.")];

        float radius = DefaultRadius;
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("radius", out var radiusValue)
            && radiusValue.ValueKind == JsonValueKind.Number
            && radiusValue.TryGetSingle(out float asked))
        {
            radius = Math.Clamp(asked, 1f, MaxRadius);
        }

        var mapId = player.World.MapId;
        var position = player.World.PlayerPosition;
        float[] centre = [position.X, position.Y, position.Z];
        var mesh = walkableGround?.ForMap(mapId);
        if (mesh is null)
        {
            return [outbox.Reply(WebMessageTypes.NavigationGround, new NavigationGroundPayload(
                mapId, Baked: false, PolygonTotal: 0, centre, radius, Truncated: false,
                Parameters: null, Polygons: [], StandingOn: null, GroundY: null, PlayerY: position.Y))];
        }

        var polygons = new List<NavigationPolygonPayload>();
        bool truncated = false;
        for (int index = 0; index < mesh.PolygonTotal; index++)
        {
            var polygon = mesh.Polygon(index);
            if (!WithinRadius(polygon.Vertices, position.X, position.Z, radius)) continue;
            if (polygons.Count == MaxPolygons)
            {
                truncated = true;
                break;
            }

            var flattened = new float[polygon.Vertices.Count * 3];
            for (int v = 0; v < polygon.Vertices.Count; v++)
            {
                var vertex = polygon.Vertices[v];
                flattened[v * 3] = vertex.X;
                flattened[(v * 3) + 1] = vertex.Y;
                flattened[(v * 3) + 2] = vertex.Z;
            }

            polygons.Add(new NavigationPolygonPayload(index, flattened, polygon.Neighbours));
        }

        int standingOn = mesh.PolygonUnder(position.X, position.Z, position.Y);
        int nearest = mesh.PolygonNear(position.X, position.Z, position.Y);
        var parameters = mesh.Parameters;
        return [outbox.Reply(WebMessageTypes.NavigationGround, new NavigationGroundPayload(
            mapId,
            Baked: true,
            mesh.PolygonTotal,
            centre,
            radius,
            truncated,
            new NavigationParametersPayload(
                parameters.CellSize, parameters.BodyRadius, parameters.StepHeight,
                parameters.MaxSlopeDegrees, parameters.Headroom),
            polygons,
            standingOn < 0 ? null : standingOn,
            standingOn < 0 ? null : mesh.HeightOf(standingOn, position.X, position.Z),
            position.Y,
            nearest < 0 ? null : mesh.HeightOf(nearest, position.X, position.Z)))];
    }

    private static bool WithinRadius(IReadOnlyList<Ubiquitous.Common.Position> vertices, float x, float z, float radius)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var vertex in vertices)
        {
            minX = MathF.Min(minX, vertex.X);
            maxX = MathF.Max(maxX, vertex.X);
            minZ = MathF.Min(minZ, vertex.Z);
            maxZ = MathF.Max(maxZ, vertex.Z);
        }

        float dx = MathF.Max(0f, MathF.Max(minX - x, x - maxX));
        float dz = MathF.Max(0f, MathF.Max(minZ - z, z - maxZ));
        return (dx * dx) + (dz * dz) <= radius * radius;
    }
}
