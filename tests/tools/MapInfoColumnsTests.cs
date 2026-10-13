using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class MapInfoColumnsTests
{
    private const string TableFormat = "uuuuufffussuuufuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var name = "test-city\0"u8.ToArray();
        var file = "world/city.wld\0"u8.ToArray();
        var pool = name.Concat(file).ToArray();
        var data = BuildRow(id: 13, nameLength: (short)name.Length, nameOffset: 0,
            fileLength: (short)file.Length, fileOffset: name.Length, pool);

        var map = Assert.Single(MapTableMapper.MapMaps(FTableDocument.Load(data), MapInfoColumns.Build114));

        Assert.Equal((ushort)13, map.Id);
        Assert.Equal("test-city", map.InternalName);
        Assert.Equal("world/city.wld", map.File);
        Assert.Equal(20, map.MinLevel);
        Assert.True(map.IsDungeon);
    }

    private static byte[] BuildRow(
        uint id, short nameLength, int nameOffset, short fileLength, int fileOffset, byte[] pool)
    {
        int cols = TableFormat.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = TableFormat[i] switch
            {
                's' => 6,
                'h' => 2,
                'c' => 1,
                _ => 4,
            };
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize;
        var data = new byte[offsetStrings + pool.Length];
        pool.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, pool.Length);
        WriteAscii(data, offsetFormat, TableFormat);

        int at = offsetEntry;
        for (int i = 0; i < cols; i++)
        {
            if (TableFormat[i] == 's')
            {
                if (i == 9)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)(offsetStrings + nameOffset));
                    BitConverter.GetBytes(nameLength).CopyTo(data, at + 4);
                }
                else if (i == 10)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)(offsetStrings + fileOffset));
                    BitConverter.GetBytes(fileLength).CopyTo(data, at + 4);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 0);
                    BitConverter.GetBytes((short)0).CopyTo(data, at + 4);
                }
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), i switch
                {
                    0 => id,
                    3 => MapTableMapper.DungeonKind,
                    4 => 20u,
                    _ => 0u,
                });
            }

            at += widths[i];
        }

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
