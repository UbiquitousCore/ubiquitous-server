using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record CreatureSpawnTableColumns(
    int Id, int MapId, int CreatureEntryId, int X, int Y, int Z, int Yaw,
    int IdleBehaviour = -1, int PatrolRouteId = -1, int WanderRadius = -1, int Leader = -1, int Animation = -1);

public static class CreatureSpawnTableMapper
{
    public static IReadOnlyList<CreatureSpawnDefinition> MapSpawns(
        FTableDocument document,
        CreatureSpawnTableColumns columns,
        IReadOnlySet<ushort> mapIds,
        IReadOnlySet<uint> creatureEntries)
    {
        var spawns = new List<CreatureSpawnDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate spawn id {id}.");
            }

            ushort mapId = RequireMapId(row, columns.MapId, i);
            uint entryId = RequireUInt(row, columns.CreatureEntryId, i, "creature entry");
            float x = RequireFloat(row, columns.X, i, "x");
            float y = RequireFloat(row, columns.Y, i, "y");
            float z = RequireFloat(row, columns.Z, i, "z");
            float yaw = RequireFloat(row, columns.Yaw, i, "yaw");

            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Spawn {id} references missing map {mapId}.");
            }

            if (!creatureEntries.Contains(entryId))
            {
                throw new FTableFormatException($"Spawn {id} references missing creature {entryId}.");
            }

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
            {
                throw new FTableFormatException($"Spawn {id} has non-finite position data.");
            }

            int idleBehaviour = columns.IdleBehaviour < 0 ? 0 : RequireSByte(row, columns.IdleBehaviour, i, "idle behaviour");
            uint patrolRouteId = columns.PatrolRouteId < 0 ? 0u : RequireUInt(row, columns.PatrolRouteId, i, "patrol route id");
            float wanderRadius = columns.WanderRadius < 0 ? 0f : RequireFloat(row, columns.WanderRadius, i, "move range");
            if (!float.IsFinite(wanderRadius))
            {
                throw new FTableFormatException($"Spawn {id} has a non-finite move range.");
            }

            uint leader = columns.Leader < 0 ? 0u : RequireUInt(row, columns.Leader, i, "leader");
            uint animation = columns.Animation < 0 ? 0u : RequireUInt(row, columns.Animation, i, "animation");

            spawns.Add(new CreatureSpawnDefinition(
                id, mapId, entryId, x, y, z, yaw, idleBehaviour, patrolRouteId, wanderRadius, leader, animation));
        }

        return ResolveLeaders(spawns, seen);
    }

    private static IReadOnlyList<CreatureSpawnDefinition> ResolveLeaders(
        List<CreatureSpawnDefinition> spawns, HashSet<uint> spawnIds)
    {
        for (int i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];
            if (spawn.LeaderSpawnId == 0) continue;
            if (spawn.LeaderSpawnId != spawn.Id && spawnIds.Contains(spawn.LeaderSpawnId)) continue;
            spawns[i] = spawn with { LeaderSpawnId = 0 };
        }

        return spawns;
    }

    private static int RequireSByte(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not sbyte value)
        {
            throw new FTableFormatException($"Row {index} has no usable spawn {field}.");
        }

        return value;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable spawn {field}.");
        }

        return value;
    }

    private static ushort RequireMapId(IReadOnlyList<object> row, int column, int index)
    {
        uint value = RequireUInt(row, column, index, "map");
        if (value > ushort.MaxValue)
        {
            throw new FTableFormatException($"Row {index} has a map id outside u16 range.");
        }

        return (ushort)value;
    }

    private static float RequireFloat(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not float value)
        {
            throw new FTableFormatException($"Row {index} has no usable spawn {field}.");
        }

        return value;
    }
}
