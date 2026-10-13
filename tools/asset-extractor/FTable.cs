using System.Buffers.Binary;
using System.Text;

namespace Ubiquitous.AssetExtractor;

public sealed class FTableFormatException : Exception
{
    public FTableFormatException(string message)
        : base(message)
    {
    }
}

public sealed record TableLayout(
    int Rows,
    int Cols,
    int RowIdsAt,
    int ColumnTypesAt,
    int RecordsAt,
    int RecordSize,
    int StringsAt,
    int StringsLength,
    string ColumnTypes);

public static class TableLayoutReader
{
    public const int HeaderSize = 44;

    public static TableLayout Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new FTableFormatException(
                $"Data is shorter than a {HeaderSize}-byte FTABLE header.");
        }

        if (Encoding.ASCII.GetString(data.Slice(0, 6)) != "FTABLE")
        {
            throw new FTableFormatException("Missing FTABLE magic.");
        }

        int rows = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4)));
        int cols = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4)));
        int rowIdsAt = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4)));
        int columnTypesAt = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4)));
        int recordsAt = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4)));
        int recordSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4)));
        int stringsAt = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36, 4)));
        int stringsLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(40, 4)));

        foreach (int offset in new[] { rowIdsAt, columnTypesAt, recordsAt, stringsAt })
        {
            if (offset < 0 || offset > data.Length)
            {
                throw new FTableFormatException("FTABLE section offset is outside the file.");
            }
        }

        if ((long)columnTypesAt + cols > data.Length)
        {
            throw new FTableFormatException("FTABLE column type string runs past end of file.");
        }

        if ((long)rowIdsAt + (long)rows * 4 > data.Length)
        {
            throw new FTableFormatException("FTABLE row-id index runs past end of file.");
        }

        if (rows > 0 && recordSize <= 0)
        {
            throw new FTableFormatException("FTABLE record size must be positive when rows exist.");
        }

        if ((long)recordsAt + (long)rows * recordSize > data.Length)
        {
            throw new FTableFormatException("FTABLE records run past end of file.");
        }

        if ((long)stringsAt + stringsLength > data.Length)
        {
            throw new FTableFormatException("FTABLE string pool runs past end of file.");
        }

        string format = Encoding.ASCII.GetString(data.Slice(columnTypesAt, cols));
        return new TableLayout(
            rows, cols, rowIdsAt, columnTypesAt, recordsAt, recordSize,
            stringsAt, stringsLength, format);
    }
}

public static class FTableRowReader
{
    public static IReadOnlyList<object> ReadRow(ReadOnlySpan<byte> data, TableLayout header, int index)
    {
        if (index < 0 || index >= header.Rows)
        {
            throw new FTableFormatException($"Row {index} is outside 0..{header.Rows}.");
        }

        long start = (long)header.RecordsAt + (long)index * header.RecordSize;
        if (start + header.RecordSize > data.Length)
        {
            throw new FTableFormatException($"Row {index} runs past end of file.");
        }

        var cells = new List<object>(header.Cols);
        long pos = start;
        foreach (char column in header.ColumnTypes)
        {
            if (column == 's')
            {
                if (pos + 6 > data.Length)
                {
                    throw new FTableFormatException($"Row {index} cell runs past end of file.");
                }

                int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)pos, 4)));
                short length = BinaryPrimitives.ReadInt16LittleEndian(data.Slice((int)pos + 4, 2));
                cells.Add(ReadString(data, offset, length));
                pos += 6;
                continue;
            }

            int width = ColumnWidth(column);
            if (pos + width > data.Length)
            {
                throw new FTableFormatException($"Row {index} cell runs past end of file.");
            }

            cells.Add(ReadScalar(data, (int)pos, column));
            pos += width;
        }

        return cells;
    }

    public static IReadOnlyDictionary<uint, IReadOnlyList<object>> ReadByKey(
        ReadOnlySpan<byte> data, TableLayout header)
    {
        var rows = new Dictionary<uint, IReadOnlyList<object>>();
        for (int i = 0; i < header.Rows; i++)
        {
            long keyAt = (long)header.RowIdsAt + (long)i * 4;
            if (keyAt + 4 > data.Length)
            {
                throw new FTableFormatException($"Row-id index entry {i} runs past end of file.");
            }

            uint key = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)keyAt, 4));
            if (!rows.ContainsKey(key))
            {
                rows[key] = ReadRow(data, header, i);
            }
        }

        return rows;
    }

    public static int ColumnWidth(char column) => column switch
    {
        'k' or 'u' or 'i' or 'f' => 4,
        'h' => 2,
        'c' => 1,
        _ => 4,
    };

    public static string ReadString(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (length <= 0 || offset <= 0 || (long)offset + length > data.Length)
        {
            return string.Empty;
        }

        int end = offset + length;
        while (end > offset && data[end - 1] == 0)
        {
            end--;
        }

        var bytes = data.Slice(offset, end - offset);
        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWindows1252(bytes);
        }
    }

    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    private static string DecodeWindows1252(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = Windows1252ByteToChar(bytes[i]);
        }

        return new string(chars);
    }

    private static char Windows1252ByteToChar(byte value) => value switch
    {
        0x80 => '€',
        0x82 => '‚',
        0x83 => 'ƒ',
        0x84 => '„',
        0x85 => '…',
        0x86 => '†',
        0x87 => '‡',
        0x88 => 'ˆ',
        0x89 => '‰',
        0x8A => 'Š',
        0x8B => '‹',
        0x8C => 'Œ',
        0x8E => 'Ž',
        0x91 => '‘',
        0x92 => '’',
        0x93 => '“',
        0x94 => '”',
        0x95 => '•',
        0x96 => '–',
        0x97 => '—',
        0x98 => '˜',
        0x99 => '™',
        0x9A => 'š',
        0x9B => '›',
        0x9C => 'œ',
        0x9E => 'ž',
        0x9F => 'Ÿ',
        _ => (char)value,
    };

    private static object ReadScalar(ReadOnlySpan<byte> data, int at, char column) => column switch
    {
        'i' => (object)BinaryPrimitives.ReadInt32LittleEndian(data.Slice(at, 4)),
        'f' => (object)BinaryPrimitives.ReadSingleLittleEndian(data.Slice(at, 4)),
        'c' => (object)unchecked((sbyte)data[at]),
        'h' => (object)BinaryPrimitives.ReadInt16LittleEndian(data.Slice(at, 2)),
        _ => (object)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4)),
    };
}

public sealed class FTableDocument
{
    public TableLayout Header { get; }
    public IReadOnlyList<IReadOnlyList<object>> Rows { get; }
    public IReadOnlyDictionary<uint, IReadOnlyList<object>> ByKey { get; }

    private FTableDocument(
        TableLayout header,
        IReadOnlyList<IReadOnlyList<object>> rows,
        IReadOnlyDictionary<uint, IReadOnlyList<object>> byKey)
    {
        Header = header;
        Rows = rows;
        ByKey = byKey;
    }

    public static FTableDocument Load(ReadOnlySpan<byte> data)
    {
        var header = TableLayoutReader.Read(data);
        var rows = new List<IReadOnlyList<object>>(header.Rows);
        var byKey = new Dictionary<uint, IReadOnlyList<object>>();
        for (int i = 0; i < header.Rows; i++)
        {
            long keyAt = (long)header.RowIdsAt + (long)i * 4;
            if (keyAt + 4 > data.Length)
            {
                throw new FTableFormatException($"Row-id index entry {i} runs past end of file.");
            }

            uint key = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)keyAt, 4));
            var row = FTableRowReader.ReadRow(data, header, i);
            rows.Add(row);
            byKey.TryAdd(key, row);
        }

        return new FTableDocument(header, rows, byKey);
    }

    public FTableDocument WithRows(IEnumerable<IReadOnlyList<object>> rows)
    {
        var kept = rows.ToList();
        var byKey = new Dictionary<uint, IReadOnlyList<object>>();
        foreach (var row in kept)
        {
            if (row.Count > 0 && row[0] is uint key)
            {
                byKey.TryAdd(key, row);
            }
        }

        return new FTableDocument(Header, kept, byKey);
    }
}
