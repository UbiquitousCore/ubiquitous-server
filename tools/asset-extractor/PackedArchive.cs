using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Ubiquitous.AssetExtractor;

public sealed class PackedArchiveFormatException(string message) : Exception(message);

public sealed class PackedArchive : IDisposable
{
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const int CentralDirectoryRecordSize = 46;
    private const int EndOfCentralDirectorySize = 22;
    private const int LocalHeaderSize = 30;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private PackedArchive(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        ReadCentralDirectory();
    }

    private static ReadOnlySpan<byte> SubstitutedLocalHeaderMagic => "GBMP"u8;

    private static ReadOnlySpan<byte> StandardLocalHeaderMagic => "PK\u0003\u0004"u8;

    public IReadOnlyCollection<string> Names => _entries.Keys;

    public static PackedArchive Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new PackedArchive(File.OpenRead(path), ownsStream: true);
    }

    public static PackedArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new PackedArchiveFormatException("A packed archive is read back to front and needs a seekable stream.");
        }

        return new PackedArchive(stream, ownsStream: false);
    }

    public byte[] Read(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
        {
            throw new PackedArchiveFormatException($"No entry '{name}' in this archive.");
        }

        _stream.Position = entry.LocalHeaderOffset;
        var header = ReadExactly(LocalHeaderSize, $"local header of '{name}'");
        var magic = header.AsSpan(0, 4);
        if (!magic.SequenceEqual(SubstitutedLocalHeaderMagic) && !magic.SequenceEqual(StandardLocalHeaderMagic))
        {
            throw new PackedArchiveFormatException($"Entry '{name}' has neither local header signature this reader knows.");
        }

        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26, 2));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28, 2));
        _stream.Position = entry.LocalHeaderOffset + LocalHeaderSize + nameLength + extraLength;
        var compressed = ReadExactly(entry.CompressedSize, $"data of '{name}'");

        return entry.Method switch
        {
            0 => compressed,
            8 => Inflate(compressed, entry.UncompressedSize, name),
            _ => throw new PackedArchiveFormatException(
                $"Entry '{name}' uses compression method {entry.Method}, which this reader does not know."),
        };
    }

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }

    private static byte[] Inflate(byte[] compressed, int uncompressedSize, string name)
    {
        var output = new byte[uncompressedSize];
        using var source = new MemoryStream(compressed, writable: false);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        int total = 0;
        while (total < uncompressedSize)
        {
            int read = deflate.Read(output, total, uncompressedSize - total);
            if (read <= 0)
            {
                throw new PackedArchiveFormatException($"Entry '{name}' decompressed to fewer bytes than declared.");
            }

            total += read;
        }

        return output;
    }

    private byte[] ReadExactly(int count, string what)
    {
        var buffer = new byte[count];
        int total = 0;
        while (total < count)
        {
            int read = _stream.Read(buffer, total, count - total);
            if (read <= 0) throw new PackedArchiveFormatException($"The {what} runs past the end of the archive.");
            total += read;
        }

        return buffer;
    }

    private void ReadCentralDirectory()
    {
        long length = _stream.Length;
        int window = (int)Math.Min(length, EndOfCentralDirectorySize + ushort.MaxValue);
        _stream.Position = length - window;
        var tail = ReadExactly(window, "end of the archive");

        int eocd = -1;
        for (int i = window - EndOfCentralDirectorySize; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) == EndOfCentralDirectorySignature)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0) throw new PackedArchiveFormatException("No end-of-central-directory record found.");

        int total = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10, 2));
        int directorySize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12, 4)));
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16, 4));
        if (directoryOffset < 0 || directoryOffset + directorySize > length)
        {
            throw new PackedArchiveFormatException("The central directory lies outside the archive.");
        }

        _stream.Position = directoryOffset;
        var directory = ReadExactly(directorySize, "central directory");

        int cursor = 0;
        for (int i = 0; i < total; i++)
        {
            if (cursor + CentralDirectoryRecordSize > directory.Length)
            {
                throw new PackedArchiveFormatException($"Central directory record {i} runs past the directory.");
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(cursor, 4)) != CentralDirectorySignature)
            {
                throw new PackedArchiveFormatException($"Central directory record {i} has a bad signature.");
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(cursor + 10, 2));
            int compressedSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(cursor + 20, 4)));
            int uncompressedSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(cursor + 24, 4)));
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(cursor + 28, 2));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(cursor + 30, 2));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(cursor + 32, 2));
            long localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(cursor + 42, 4));

            int nameAt = cursor + CentralDirectoryRecordSize;
            if (nameAt + nameLength > directory.Length)
            {
                throw new PackedArchiveFormatException($"Central directory record {i} names an entry past the directory.");
            }

            var name = Encoding.ASCII.GetString(directory.AsSpan(nameAt, nameLength)).Replace('\\', '/');
            _entries.TryAdd(name, new Entry(method, compressedSize, uncompressedSize, localHeaderOffset));
            cursor = nameAt + nameLength + extraLength + commentLength;
        }
    }

    private readonly record struct Entry(ushort Method, int CompressedSize, int UncompressedSize, long LocalHeaderOffset);
}
