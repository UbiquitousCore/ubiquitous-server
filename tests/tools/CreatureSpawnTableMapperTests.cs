using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class CreatureSpawnTableMapperTests
{
    private static readonly CreatureSpawnTableColumns Columns = new(0, 1, 2, 3, 4, 5, 6);
    private static readonly HashSet<ushort> Maps = [7];
    private static readonly HashSet<uint> Creatures = [12];

    [Fact]
    public void MapSpawns_ConvertsRowsWithResolvedReferences()
    {
        var data = BuildTable(rows: 2);
        WriteSpawn(data, rows: 2, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0.5f);
        WriteSpawn(data, rows: 2, row: 1, id: 102, map: 7, entry: 12, x: 4, y: 5, z: 6, yaw: 1f);

        var spawns = CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures);

        Assert.Equal(2, spawns.Count);
        Assert.Equal(new Ubiquitous.Data.CreatureSpawnDefinition(101, 7, 12, 1, 2, 3, 0.5f), spawns[0]);
    }

    [Fact]
    public void MapSpawns_RejectsMissingMapReference()
    {
        var data = BuildTable(rows: 1);
        WriteSpawn(data, rows: 1, row: 0, id: 101, map: 99, entry: 12, x: 1, y: 2, z: 3, yaw: 0);

        var error = Assert.Throws<FTableFormatException>(
            () => CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures));

        Assert.Contains("missing map 99", error.Message);
    }

    [Fact]
    public void MapSpawns_RejectsMissingCreatureTemplate()
    {
        var data = BuildTable(rows: 1);
        WriteSpawn(data, rows: 1, row: 0, id: 101, map: 7, entry: 99, x: 1, y: 2, z: 3, yaw: 0);

        var error = Assert.Throws<FTableFormatException>(
            () => CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures));

        Assert.Contains("missing creature 99", error.Message);
    }

    [Fact]
    public void MapSpawns_RejectsNonFiniteCoordinate()
    {
        var data = BuildTable(rows: 1);
        WriteSpawn(data, rows: 1, row: 0, id: 101, map: 7, entry: 12, x: float.NaN, y: 2, z: 3, yaw: 0);

        Assert.Throws<FTableFormatException>(
            () => CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures));
    }

    [Fact]
    public void MapSpawns_RejectsDuplicateSpawnId()
    {
        var data = BuildTable(rows: 2);
        WriteSpawn(data, rows: 2, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0);
        WriteSpawn(data, rows: 2, row: 1, id: 101, map: 7, entry: 12, x: 4, y: 5, z: 6, yaw: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures));

        Assert.Contains("Duplicate spawn id 101", error.Message);
    }

    [Fact]
    public void MapSpawns_RejectsWrongCellType()
    {
        var data = BuildTable(rows: 1);
        WriteSpawn(data, rows: 1, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0);

        var shifted = Columns with { Id = 3 };

        Assert.Throws<FTableFormatException>(
            () => CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), shifted, Maps, Creatures));
    }

    [Fact]
    public void MapSpawns_ReadsIdleBehaviourPatrolRouteAndMoveRangeWhenMapped()
    {
        const string format = "uuuffffcuf";
        var columnsWithWander = Columns with { IdleBehaviour = 7, PatrolRouteId = 8, WanderRadius = 9 };
        int entrySize = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 4 + 4;
        var data = BuildTable(rows: 1, format, entrySize);
        WriteSpawn(data, format, entrySize, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0);
        int baseAt = 44 + 1 * 4 + format.Length + 0 * entrySize;
        data[baseAt + 28] = unchecked((byte)(sbyte)1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(baseAt + 29, 4), 55);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(baseAt + 33, 4), 4.5f);

        var spawns = CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), columnsWithWander, Maps, Creatures);

        var spawn = Assert.Single(spawns);
        Assert.Equal(1, spawn.IdleBehaviour);
        Assert.Equal(55u, spawn.PatrolRouteId);
        Assert.Equal(4.5f, spawn.WanderRadius);
    }

    [Fact]
    public void MapSpawns_DefaultsWanderColumnsWhenUnmapped()
    {
        var data = BuildTable(rows: 1);
        WriteSpawn(data, format: "uuuffff", entrySize: 28, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0);

        var spawn = Assert.Single(CreatureSpawnTableMapper.MapSpawns(FTableDocument.Load(data), Columns, Maps, Creatures));

        Assert.Equal(0, spawn.IdleBehaviour);
        Assert.Equal(0u, spawn.PatrolRouteId);
        Assert.Equal(0f, spawn.WanderRadius);
        Assert.Equal(0u, spawn.LeaderSpawnId);
    }

    [Fact]
    public void MapSpawns_ReadsALeaderThatNamesAnotherSpawn()
    {
        var data = TwoSpawns(leaderOfFirst: 102, leaderOfSecond: 0);

        var spawns = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), ColumnsWithLeader, Maps, Creatures);

        Assert.Equal(102u, spawns[0].LeaderSpawnId);
        Assert.Equal(0u, spawns[1].LeaderSpawnId);
    }

    [Fact]
    public void MapSpawns_DropsALeaderThatNamesNoSpawnInsteadOfFailing()
    {
        var data = TwoSpawns(leaderOfFirst: 999, leaderOfSecond: 0);

        var spawns = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), ColumnsWithLeader, Maps, Creatures);

        Assert.Equal(2, spawns.Count);
        Assert.Equal(0u, spawns[0].LeaderSpawnId);
    }

    [Fact]
    public void MapSpawns_DropsASelfReferentialLeader()
    {
        var data = TwoSpawns(leaderOfFirst: 101, leaderOfSecond: 0);

        var spawns = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), ColumnsWithLeader, Maps, Creatures);

        Assert.Equal(0u, spawns[0].LeaderSpawnId);
    }

    private static readonly CreatureSpawnTableColumns ColumnsWithLeader = Columns with { Leader = 7 };

    private static byte[] TwoSpawns(uint leaderOfFirst, uint leaderOfSecond)
    {
        const string format = "uuuffffu";
        const int entrySize = 32;
        var data = BuildTable(rows: 2, format, entrySize);
        WriteSpawn(data, format, entrySize, rows: 2, row: 0, id: 101, map: 7, entry: 12, x: 1, y: 2, z: 3, yaw: 0);
        WriteSpawn(data, format, entrySize, rows: 2, row: 1, id: 102, map: 7, entry: 12, x: 4, y: 5, z: 6, yaw: 0);
        int rowBase = 44 + 2 * 4 + format.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowBase + 28, 4), leaderOfFirst);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowBase + entrySize + 28, 4), leaderOfSecond);
        return data;
    }

    private static void WriteSpawn(
        byte[] data, int rows, int row, uint id, uint map, uint entry,
        float x, float y, float z, float yaw) =>
        WriteSpawn(data, "uuuffff", 28, rows, row, id, map, entry, x, y, z, yaw);

    private static void WriteSpawn(
        byte[] data, string format, int entrySize, int row, uint id, uint map, uint entry,
        float x, float y, float z, float yaw) =>
        WriteSpawn(data, format, entrySize, rows: 1, row, id, map, entry, x, y, z, yaw);

    private static void WriteSpawn(
        byte[] data, string format, int entrySize, int rows, int row, uint id, uint map, uint entry,
        float x, float y, float z, float yaw)
    {
        int at = 44 + rows * 4 + format.Length + row * entrySize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), map);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), entry);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 20, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 24, 4), yaw);
    }

    private static byte[] BuildTable(int rows) => BuildTable(rows, "uuuffff", 28);

    private static byte[] BuildTable(int rows, string format, int entrySize)
    {
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, format);
        return data;
    }

    private static void WriteU32(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
    }

    private static void WriteAscii(byte[] data, int at, string text)
    {
        for (int i = 0; i < text.Length && at + i < data.Length; i++)
        {
            data[at + i] = (byte)text[i];
        }
    }
}
