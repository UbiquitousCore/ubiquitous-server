using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class StorageUpgradeColumnsTests
{
    private const string Build114Format = "uuuuuuu";

    [Fact]
    public void Build114_MapsABagRungShapedLikeTheTable()
    {
        var data = BuildTable(new[]
        {
            new Dictionary<int, uint> { [0] = 4u, [1] = 0u, [2] = 18u, [3] = 0u, [4] = 10000u, [5] = 0u, [6] = 12u },
        });

        var upgrade = Assert.Single(StorageUpgradeTableMapper.MapUpgrades(FTableDocument.Load(data), StorageUpgradeColumns.Build114));

        Assert.Equal(4u, upgrade.Id);
        Assert.Equal("bag", upgrade.Area);
        Assert.Equal(18u, upgrade.SlotCount);
        Assert.Equal(12u, upgrade.FromSlotCount);
        Assert.Equal(0u, upgrade.PriceCurrency);
        Assert.Equal(10000u, upgrade.PriceAmount);
    }

    [Fact]
    public void Build114_MapsABankRung()
    {
        var data = BuildTable(new[]
        {
            new Dictionary<int, uint> { [0] = 1u, [1] = 1u, [2] = 36u, [3] = 0u, [4] = 100000u, [5] = 0u, [6] = 24u },
        });

        var upgrade = Assert.Single(StorageUpgradeTableMapper.MapUpgrades(FTableDocument.Load(data), StorageUpgradeColumns.Build114));

        Assert.Equal("bank", upgrade.Area);
        Assert.Equal(36u, upgrade.SlotCount);
        Assert.Equal(24u, upgrade.FromSlotCount);
    }

    [Fact]
    public void Build114_RejectsAnUnrecognizedStorageType()
    {
        var data = BuildTable(new[]
        {
            new Dictionary<int, uint> { [0] = 1u, [1] = 2u, [2] = 36u, [3] = 0u, [4] = 100000u, [5] = 0u, [6] = 24u },
        });

        Assert.Throws<FTableFormatException>(() =>
            StorageUpgradeTableMapper.MapUpgrades(FTableDocument.Load(data), StorageUpgradeColumns.Build114));
    }

    private static byte[] BuildTable(IReadOnlyList<Dictionary<int, uint>> rows)
    {
        int cols = Build114Format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = 4;
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        int rowCount = rows.Count;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rowCount * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize * rowCount;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rowCount);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, Build114Format);

        int at = offsetEntry;
        foreach (var row in rows)
        {
            for (int c = 0; c < cols; c++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), row.GetValueOrDefault(c));
                at += widths[c];
            }
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
