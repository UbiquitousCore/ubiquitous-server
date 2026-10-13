using System.IO.Compression;
using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class PackedArchiveTests
{
    [Fact]
    public void AnEntry_ComesBackByName()
    {
        var bytes = PackedArchiveFixture.Build(
            ("world/town/items/0000_0000.itm", "first"u8.ToArray()),
            ("world/town/terrain/0000_0000.trn", "second"u8.ToArray()));

        using var archive = PackedArchive.Open(new MemoryStream(bytes));

        Assert.Equal(2, archive.Names.Count);
        Assert.Equal("first"u8.ToArray(), archive.Read("world/town/items/0000_0000.itm"));
        Assert.Equal("second"u8.ToArray(), archive.Read("world/town/terrain/0000_0000.trn"));
    }

    [Fact]
    public void AnArchiveWithTheOrdinaryHeaderSignature_ReadsTheSameWay()
    {
        var bytes = PackedArchiveFixture.Build(
            standardSignature: true, ("world/town/items/0000_0000.itm", "first"u8.ToArray()));

        using var archive = PackedArchive.Open(new MemoryStream(bytes));

        Assert.Equal("first"u8.ToArray(), archive.Read("world/town/items/0000_0000.itm"));
    }

    [Fact]
    public void AnEntryWhoseHeaderIsNeitherSignature_IsRefusedRatherThanRead()
    {
        var bytes = PackedArchiveFixture.Build(("world/town/items/0000_0000.itm", "first"u8.ToArray()));
        bytes[PackedArchiveFixture.IndexOf(bytes, "GBMP"u8.ToArray())] = (byte)'X';

        using var archive = PackedArchive.Open(new MemoryStream(bytes));

        Assert.Throws<PackedArchiveFormatException>(() => archive.Read("world/town/items/0000_0000.itm"));
    }

    [Fact]
    public void SomethingThatIsNotAnArchiveAtAll_SaysSo()
    {
        Assert.Throws<PackedArchiveFormatException>(
            () => PackedArchive.Open(new MemoryStream(new byte[512])));
    }

    [Fact]
    public void AnEntryThisArchiveDoesNotCarry_SaysWhichOne()
    {
        var bytes = PackedArchiveFixture.Build(("world/town/items/0000_0000.itm", "first"u8.ToArray()));
        using var archive = PackedArchive.Open(new MemoryStream(bytes));

        var failure = Assert.Throws<PackedArchiveFormatException>(() => archive.Read("world/town/items/0001_0000.itm"));

        Assert.Contains("0001_0000.itm", failure.Message, StringComparison.Ordinal);
    }
}

internal static class PackedArchiveFixture
{
    public static byte[] Build(params (string Name, byte[] Bytes)[] entries) =>
        Build(standardSignature: false, entries);

    public static byte[] Build(bool standardSignature, params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        }

        var archive = stream.ToArray();
        if (standardSignature) return archive;

        var standard = Encoding.ASCII.GetBytes("PK\u0003\u0004");
        var substituted = "GBMP"u8.ToArray();
        for (int i = 0; i + 4 <= archive.Length; i++)
        {
            if (archive.AsSpan(i, 4).SequenceEqual(standard)) substituted.CopyTo(archive, i);
        }

        return archive;
    }

    public static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        }

        return -1;
    }
}
