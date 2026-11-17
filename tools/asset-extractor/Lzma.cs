using SharpCompress.Compressors.LZMA;

namespace Ubiquitous.AssetExtractor;

public sealed class LzmaFormatException(string message) : Exception(message);

// Pack members stored with ZIP compression method 14. The LZMA stream itself
// is SharpCompress's to decode; the framing around it is APPNOTE's.
public static class Lzma
{
    public const ushort ZipMethod = 14;

    // General-purpose flag bit 1 on a method 14 member: the stream ends with an end marker.
    public const ushort ZipEndMarkerFlag = 0x0002;

    private const int PropertiesSize = 5;
    private const int FramingSize = 4;

    // Version (2 bytes), properties size (2), properties, stream. With or without
    // an end marker, the member is exactly `size` bytes once decoded.
    public static byte[] DecodeZip(ReadOnlySpan<byte> data, int size, bool endMarker)
    {
        if (data.Length < FramingSize + PropertiesSize) throw new LzmaFormatException("Truncated LZMA header.");
        int propertiesSize = data[2] | (data[3] << 8);
        if (propertiesSize != PropertiesSize)
        {
            throw new LzmaFormatException($"Unexpected LZMA properties size {propertiesSize}.");
        }

        var stream = data[(FramingSize + PropertiesSize)..].ToArray();
        using var input = new MemoryStream(stream, writable: false);
        using var output = new MemoryStream(size);
        try
        {
            var decoder = new Decoder();
            decoder.SetDecoderProperties(data.Slice(FramingSize, PropertiesSize).ToArray());
            decoder.Code(input, output, stream.Length, endMarker ? -1 : size, progress: null);
        }
        catch (Exception ex) when (ex is not LzmaFormatException)
        {
            throw new LzmaFormatException($"LZMA stream does not decode: {ex.Message}");
        }

        if (output.Length != size)
        {
            throw new LzmaFormatException($"LZMA stream decoded to {output.Length} bytes; {size} stated.");
        }

        return output.ToArray();
    }
}
