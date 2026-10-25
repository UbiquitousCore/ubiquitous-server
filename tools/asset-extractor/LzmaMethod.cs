using System.Buffers.Binary;
using SharpCompress.Compressors.LZMA;

namespace Ubiquitous.AssetExtractor;

/// <summary>
/// ZIP compression method 14 (LZMA), which newer Gameloft packs (2.5.0 and up)
/// use for every entry. Layout per APPNOTE 5.2.6: LZMA version (2 bytes),
/// properties size (2 bytes, little-endian), LZMA properties, then the raw
/// LZMA stream. The caller supplies its own error type so both archive
/// readers keep throwing the exception their tests expect.
/// </summary>
public static class LzmaMethod
{
    public static byte[] Decode(
        string name, ReadOnlySpan<byte> payload, int uncompressedSize,
        Func<string, Exception> error)
    {
        if (payload.Length < 4)
        {
            throw error($"Entry '{name}' is too short for an LZMA header.");
        }

        int propertiesSize = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
        if (propertiesSize != 5)
        {
            throw error($"Entry '{name}' has an LZMA properties block of {propertiesSize} bytes; only 5 are supported.");
        }

        if (payload.Length < 4 + propertiesSize)
        {
            throw error($"Entry '{name}' LZMA header runs past the entry data.");
        }

        byte[] properties = payload.Slice(4, propertiesSize).ToArray();
        byte[] compressed = payload.Slice(4 + propertiesSize).ToArray();

        using var input = new MemoryStream(compressed, writable: false);
        using var output = new MemoryStream(uncompressedSize);
        var decoder = new Decoder();
        decoder.SetDecoderProperties(properties);
        decoder.Code(input, output, compressed.Length, uncompressedSize, progress: null);

        if (output.Length != uncompressedSize)
        {
            throw error($"Entry '{name}' decompressed to {output.Length} bytes; {uncompressedSize} declared.");
        }

        return output.ToArray();
    }
}
