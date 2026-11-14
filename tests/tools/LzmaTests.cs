using System.Buffers.Binary;
using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

// The streams below were made for these tests with Python's `lzma` module
// (raw LZMA1, 64 KiB dictionary) from the inputs built beside them, so the
// expected output is exact and no game data is needed. That encoder always
// ends its stream with an end marker, which lets one stream check both ways
// a member can end: on the marker, and on its stated size before it.
public class LzmaTests
{
    // lc=3, lp=0, pb=2 with a 64 KiB dictionary, as the properties byte 0x5d says.
    private static readonly byte[] PropsDefault = Convert.FromHexString("5d00000100");

    // lc=0, lp=2, pb=0.
    private static readonly byte[] PropsLp2 = Convert.FromHexString("1200000100");

    private static readonly byte[] Prose = Encoding.ASCII.GetBytes(
        string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 3))
        + "Pack your box with five dozen liquor jugs.");

    private static readonly byte[] ProseLzma = Convert.FromHexString(
        "002a1a08a2032566f14b78c5a205ff2ee6d9d2201aad34f8e21de84136fadc0669bb3ce410342709ebb366e3ed375ae812ed33b251bf292030800450dd2f156646e794cf5e6bd85234d42347b3fc3c83dc76177d511fffadbd8000");

    private static readonly byte[] AllBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

    private static readonly byte[] Repeats =
    [
        .. Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("abcabcabd", 40))
            + string.Concat(Enumerable.Repeat("xyzxyzxy", 30))
            + string.Concat(Enumerable.Repeat("abcabcabd", 10))),
        .. AllBytes,
        .. AllBytes,
    ];

    private static readonly byte[] RepeatsLzma = Convert.FromHexString(
        "00309888a5836e0e016066b8826eccc5ae197a5e178cfb14ec3a5d47a38d0ee9805a6f017a99907baa6188cab9c0a88bfd320a9f37b8da5da88a0fc52c08ec5b1c1cb66498bfd966f52ad37893c5a866709c49e8c87302f71588f25bcb8acd09a4c77f9c7d98b925915b086a76c34436ef0462d5c45d8ab03463fd90c5397d1e8dc442fafb43eee0c7db1ce643458a10bff24249d0c3a15d3369bca8db54f5366999672994cb603daabb30dedac0d7474824db9d724759ac2ff1b6ed9ac633d9e6a3608493a9e1576d3040e52712af2f4a0869483dc3bb158295090e2c1765d4cb1f0454e8268da8d827aea50c059558324d8e13f329173610e4370fcc9ccc3e9c54137eaddffcb7b214");

    private static readonly byte[] Lp2 = Encoding.ASCII.GetBytes(
        string.Concat(Enumerable.Repeat("lp and pb set ", 20)));

    private static readonly byte[] Lp2Lzma = Convert.FromHexString("00361ce06839d1bbc7b9ec5450f3ee474efd11300a3ffffeaf5800");

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in new[] { "prose", "repeats", "lp2" })
        {
            data.Add(name, true);
            data.Add(name, false);
        }

        return data;
    }

    private static (byte[] Props, byte[] Stream, byte[] Plain) Case(string name) => name switch
    {
        "prose" => (PropsDefault, ProseLzma, Prose),
        "repeats" => (PropsDefault, RepeatsLzma, Repeats),
        _ => (PropsLp2, Lp2Lzma, Lp2),
    };

    [Fact]
    public void ThePropertiesByte_SplitsIntoLcLpAndPb_AndTheDictionarySizeFollows()
    {
        Assert.Equal(new LzmaProperties(3, 0, 2, 1 << 16), LzmaProperties.Parse(PropsDefault));
        Assert.Equal(new LzmaProperties(0, 2, 0, 1 << 16), LzmaProperties.Parse(PropsLp2));
    }

    [Fact]
    public void APropertiesBytePastTheLargestLcLpAndPb_IsRefused()
    {
        Assert.Throws<LzmaFormatException>(() => LzmaProperties.Parse(Convert.FromHexString("e100000100")));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AStream_DecodesToItsMarkerOrToItsStatedSize(string name, bool endMarker)
    {
        var (props, stream, plain) = Case(name);

        Assert.Equal(plain, Lzma.Decode(stream, LzmaProperties.Parse(props), plain.Length, endMarker));
    }

    [Fact]
    public void AnEndMarkerBeforeTheStatedSize_IsRefused()
    {
        Assert.Throws<LzmaFormatException>(() =>
            Lzma.Decode(ProseLzma, LzmaProperties.Parse(PropsDefault), Prose.Length + 10, endMarker: true));
    }

    [Fact]
    public void AStreamWhoseFirstByteIsNotZero_IsRefused()
    {
        var bad = ProseLzma.ToArray();
        bad[0] = 1;

        Assert.Throws<LzmaFormatException>(() =>
            Lzma.Decode(bad, LzmaProperties.Parse(PropsDefault), Prose.Length, endMarker: false));
    }

    [Fact]
    public void TheZipFraming_IsVersionThenPropertiesSizeThenPropertiesThenStream()
    {
        byte[] framed = [.. Convert.FromHexString("09140500"), .. PropsDefault, .. RepeatsLzma];

        Assert.Equal(Repeats, Lzma.DecodeZip(framed, Repeats.Length, endMarker: false));
    }

    [Fact]
    public void ZipFramingWhosePropertiesAreNotFiveBytes_IsRefused()
    {
        byte[] framed = [.. Convert.FromHexString("09140400"), .. PropsDefault, .. RepeatsLzma];

        Assert.Throws<LzmaFormatException>(() => Lzma.DecodeZip(framed, Repeats.Length, endMarker: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Lzma.ZipEndMarkerFlag)]
    public void AMethod14Member_ReadsBackFromAPackAndFromATableArchive(ushort flags)
    {
        var pack = LzmaPack("tables/a.tbl", Repeats, RepeatsLzma, flags, Crc32.Compute(Repeats));

        using var archive = PackedArchive.Open(new MemoryStream(pack));
        Assert.Equal(Repeats, archive.Read("tables/a.tbl"));
        Assert.Equal(Repeats, TableArchiveReader.ReadEntries(pack)["tables/a.tbl"]);
    }

    [Fact]
    public void AMethod14MemberWhoseBytesDoNotMatchItsCrc_IsRefused()
    {
        var pack = LzmaPack("tables/a.tbl", Prose, ProseLzma, 0, Crc32.Compute(Prose) ^ 1);

        using var archive = PackedArchive.Open(new MemoryStream(pack));
        Assert.Throws<PackedArchiveFormatException>(() => archive.Read("tables/a.tbl"));
        Assert.Throws<TableArchiveFormatException>(() => TableArchiveReader.ReadEntries(pack));
    }

    [Fact]
    public void Crc32_IsTheZipChecksum()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    // Points at the operator's own client build (an .ipa, its app directory or
    // a directory holding the packs); skipped when unset.
    [Fact]
    public void EveryTableInTheOperatorsBuild_DecodesToTheSizeAndCrcItsDirectoryRecords()
    {
        if (Environment.GetEnvironmentVariable("UBIQUITOUS_TEST_CLIENT_BUILD") is not { Length: > 0 } build) return;
        var opened = BuildPacks.Open(build, ["tables.bin"]);
        try
        {
            var tables = Assert.Single(opened);
            var entries = TableArchiveReader.ReadEntries(tables.ReadAllBytes());
            Assert.NotEmpty(entries);
            Assert.All(entries.Keys, name => FTableDocument.Load(entries[name]));
        }
        finally
        {
            foreach (var pack in opened) pack.Dispose();
        }
    }

    // One member stored with method 14, in a GBMP-stamped pack.
    private static byte[] LzmaPack(string name, byte[] plain, byte[] stream, ushort flags, uint crc)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] data = [.. Convert.FromHexString("09140500"), .. PropsDefault, .. stream];

        var local = new byte[30 + nameBytes.Length];
        "GBMP"u8.CopyTo(local);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), Lzma.ZipMethod);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)plain.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)nameBytes.Length);
        nameBytes.CopyTo(local, 30);

        var central = new byte[46 + nameBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(central, 0x02014b50);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), Lzma.ZipMethod);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(16), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), (uint)plain.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)nameBytes.Length);
        nameBytes.CopyTo(central, 46);

        var end = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), (uint)central.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), (uint)(local.Length + data.Length));
        return [.. local, .. data, .. central, .. end];
    }
}
