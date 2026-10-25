using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Ubiquitous.AssetExtractor;

public sealed class TableArchiveFormatException : Exception
{
    public TableArchiveFormatException(string message)
        : base(message)
    {
    }
}

public static class TableArchiveReader
{
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const int EndOfCentralDirectorySize = 22;
    private const int CentralDirectoryRecordSize = 46;
    private const int LocalHeaderSize = 30;

    private static ReadOnlySpan<byte> LocalHeaderMagic => "GBMP"u8;

    public static IReadOnlyDictionary<string, byte[]> ReadEntries(ReadOnlySpan<byte> archive)
    {
        int eocd = FindEndOfCentralDirectory(archive);
        int totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(eocd + 10, 2));
        int centralDirectoryOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(eocd + 16, 4)));

        var entries = new Dictionary<string, byte[]>(totalEntries);
        int cursor = centralDirectoryOffset;
        for (int i = 0; i < totalEntries; i++)
        {
            if (cursor + CentralDirectoryRecordSize > archive.Length)
            {
                throw new TableArchiveFormatException($"Central directory record {i} runs past end of file.");
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(cursor, 4)) != CentralDirectorySignature)
            {
                throw new TableArchiveFormatException($"Central directory record {i} has a bad signature.");
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(cursor + 10, 2));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(cursor + 20, 4));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(cursor + 24, 4));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(cursor + 28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(cursor + 30, 2));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(cursor + 32, 2));
            uint localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(cursor + 42, 4));

            int nameAt = cursor + CentralDirectoryRecordSize;
            if (nameAt + nameLength > archive.Length)
            {
                throw new TableArchiveFormatException($"Central directory record {i} name runs past end of file.");
            }

            string name = Encoding.ASCII.GetString(archive.Slice(nameAt, nameLength));
            byte[] bytes = ReadEntryData(archive, checked((int)localHeaderOffset), method,
                checked((int)compressedSize), checked((int)uncompressedSize), name);
            entries.TryAdd(name, bytes);

            cursor = nameAt + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static byte[] ReadEntryData(
        ReadOnlySpan<byte> archive, int localHeaderOffset, int method,
        int compressedSize, int uncompressedSize, string name)
    {
        if (localHeaderOffset < 0 || localHeaderOffset + LocalHeaderSize > archive.Length)
        {
            throw new TableArchiveFormatException($"Entry '{name}' has a local header outside the file.");
        }

        if (!archive.Slice(localHeaderOffset, 4).SequenceEqual(LocalHeaderMagic))
        {
            throw new TableArchiveFormatException($"Entry '{name}' is missing the packed local header signature.");
        }

        ushort localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(localHeaderOffset + 26, 2));
        ushort localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(localHeaderOffset + 28, 2));
        int dataAt = localHeaderOffset + LocalHeaderSize + localNameLength + localExtraLength;

        if (dataAt < 0 || (long)dataAt + compressedSize > archive.Length)
        {
            throw new TableArchiveFormatException($"Entry '{name}' data runs past end of file.");
        }

        var compressed = archive.Slice(dataAt, compressedSize);
        return method switch
        {
            0 => compressed.ToArray(),
            8 => Inflate(compressed, uncompressedSize, name),
            14 => LzmaMethod.Decode(name, compressed, uncompressedSize,
                message => new TableArchiveFormatException(message)),
            _ => throw new TableArchiveFormatException($"Entry '{name}' uses unsupported compression method {method}."),
        };
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed, int uncompressedSize, string name)
    {
        var output = new byte[uncompressedSize];
        using var source = new MemoryStream(compressed.ToArray(), writable: false);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        int total = 0;
        while (total < uncompressedSize)
        {
            int read = deflate.Read(output, total, uncompressedSize - total);
            if (read <= 0)
            {
                throw new TableArchiveFormatException($"Entry '{name}' decompressed to fewer bytes than declared.");
            }

            total += read;
        }

        return output;
    }

    private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> archive)
    {
        int earliest = Math.Max(0, archive.Length - EndOfCentralDirectorySize - ushort.MaxValue);
        for (int i = archive.Length - EndOfCentralDirectorySize; i >= earliest; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(i, 4)) == EndOfCentralDirectorySignature)
            {
                return i;
            }
        }

        throw new TableArchiveFormatException("No end-of-central-directory record found.");
    }
}
