namespace Ubiquitous.AssetExtractor;

// LZMA (LZMA1) decoding, written from the published LZMA specification, for
// pack members stored with ZIP compression method 14.
public sealed class LzmaFormatException(string message) : Exception(message);

public readonly record struct LzmaProperties(int Lc, int Lp, int Pb, uint DictionarySize)
{
    // The five-byte properties block: the lc/lp/pb byte, then the dictionary size.
    public static LzmaProperties Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 5) throw new LzmaFormatException("Truncated LZMA properties.");
        int d = bytes[0];
        if (d >= 9 * 5 * 5) throw new LzmaFormatException($"Bad LZMA properties byte {d}.");
        uint dictionarySize = (uint)(bytes[1] | (bytes[2] << 8) | (bytes[3] << 16) | (bytes[4] << 24));
        return new LzmaProperties(d % 9, d / 9 % 5, d / 45, dictionarySize);
    }
}

public static class Lzma
{
    public const ushort ZipMethod = 14;

    // General-purpose flag bit 1 on a method 14 member: the stream ends with an end marker.
    public const ushort ZipEndMarkerFlag = 0x0002;

    private const ushort ProbabilityInit = 1024;
    private const int States = 12;
    private const int PosStatesMax = 1 << 4;
    private const int LenToPosStates = 4;
    private const int EndPosModelIndex = 14;
    private const int FullDistances = 1 << (EndPosModelIndex >> 1);
    private const int AlignBits = 4;
    private const int MatchMinLength = 2;

    // ZIP method 14 framing: version (2 bytes), properties size (2), properties, stream.
    public static byte[] DecodeZip(ReadOnlySpan<byte> data, int size, bool endMarker)
    {
        if (data.Length < 4) throw new LzmaFormatException("Truncated LZMA header.");
        int propertiesSize = data[2] | (data[3] << 8);
        if (propertiesSize != 5) throw new LzmaFormatException($"Unexpected LZMA properties size {propertiesSize}.");
        var properties = LzmaProperties.Parse(data[4..]);
        return Decode(data[9..], properties, size, endMarker);
    }

    // A raw LZMA stream. `size` is the uncompressed size: without an end marker
    // the stream stops there; with one it may also end on the marker.
    public static byte[] Decode(ReadOnlySpan<byte> input, LzmaProperties properties, int size, bool endMarker)
    {
        var (lc, lp, pb, _) = properties;
        var output = new byte[size];
        var range = new RangeDecoder(input);

        var literal = Probabilities(0x300 << (lc + lp));
        var posSlot = Probabilities(LenToPosStates << 6);
        var posSpecial = Probabilities(1 + FullDistances - EndPosModelIndex);
        var align = Probabilities(1 << AlignBits);
        var isMatch = Probabilities(States << 4);
        var isRep = Probabilities(States);
        var isRepG0 = Probabilities(States);
        var isRepG1 = Probabilities(States);
        var isRepG2 = Probabilities(States);
        var isRep0Long = Probabilities(States << 4);
        var lengths = new LengthDecoder();
        var repLengths = new LengthDecoder();

        int pbMask = (1 << pb) - 1;
        int lpMask = (1 << lp) - 1;
        int state = 0;
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
        int pos = 0;

        while (true)
        {
            if (pos == size && !endMarker) break;
            int posState = pos & pbMask;

            if (range.Bit(input, isMatch, (state << 4) + posState) == 0)
            {
                if (pos == size) throw new LzmaFormatException("LZMA data runs past its size.");
                int previous = pos > 0 ? output[pos - 1] : 0;
                int literalBase = 0x300 * (((pos & lpMask) << lc) + (previous >> (8 - lc)));
                int symbol = 1;
                if (state >= 7)
                {
                    int matchByte = output[pos - (int)rep0 - 1];
                    while (symbol < 0x100)
                    {
                        int matchBit = (matchByte >> 7) & 1;
                        matchByte <<= 1;
                        int bit = range.Bit(input, literal, literalBase + ((1 + matchBit) << 8) + symbol);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit) break;
                    }
                }

                while (symbol < 0x100) symbol = (symbol << 1) | range.Bit(input, literal, literalBase + symbol);
                output[pos++] = (byte)symbol;
                state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                continue;
            }

            int length;
            if (range.Bit(input, isRep, state) != 0)
            {
                if (pos == 0) throw new LzmaFormatException("LZMA repeat before any data.");
                if (pos == size) throw new LzmaFormatException("LZMA data runs past its size.");
                if (range.Bit(input, isRepG0, state) == 0)
                {
                    if (range.Bit(input, isRep0Long, (state << 4) + posState) == 0)
                    {
                        state = state < 7 ? 9 : 11;
                        output[pos] = output[pos - (int)rep0 - 1];
                        pos++;
                        continue;
                    }
                }
                else
                {
                    uint distance;
                    if (range.Bit(input, isRepG1, state) == 0)
                    {
                        distance = rep1;
                    }
                    else
                    {
                        if (range.Bit(input, isRepG2, state) == 0)
                        {
                            distance = rep2;
                        }
                        else
                        {
                            distance = rep3;
                            rep3 = rep2;
                        }

                        rep2 = rep1;
                    }

                    rep1 = rep0;
                    rep0 = distance;
                }

                length = repLengths.Decode(ref range, input, posState);
                state = state < 7 ? 8 : 11;
            }
            else
            {
                rep3 = rep2;
                rep2 = rep1;
                rep1 = rep0;
                length = lengths.Decode(ref range, input, posState);
                state = state < 7 ? 7 : 10;

                int lengthState = Math.Min(length, LenToPosStates - 1);
                int slot = range.Tree(input, posSlot, lengthState << 6, 6);
                if (slot < 4)
                {
                    rep0 = (uint)slot;
                }
                else
                {
                    int directBits = (slot >> 1) - 1;
                    uint distance = (uint)((2 | (slot & 1)) << directBits);
                    if (slot < EndPosModelIndex)
                    {
                        distance += range.ReverseTree(input, posSpecial, (int)distance - slot, directBits);
                    }
                    else
                    {
                        distance += range.Direct(input, directBits - AlignBits) << AlignBits;
                        distance += range.ReverseTree(input, align, 0, AlignBits);
                    }

                    rep0 = distance;
                }

                if (rep0 == uint.MaxValue)
                {
                    if (!endMarker) throw new LzmaFormatException("Unexpected LZMA end marker.");
                    if (pos != size) throw new LzmaFormatException("LZMA end marker before the stated size.");
                    break;
                }

                if (pos == size) throw new LzmaFormatException("LZMA data runs past its size.");
                if (rep0 >= (uint)pos) throw new LzmaFormatException("LZMA distance reaches before the start.");
            }

            length += MatchMinLength;
            if (length > size - pos) throw new LzmaFormatException("LZMA match runs past its size.");
            // Byte by byte: a match may overlap the bytes it is producing.
            int from = pos - (int)rep0 - 1;
            int end = pos + length;
            while (pos < end) output[pos++] = output[from++];
        }

        return output;
    }

    private static ushort[] Probabilities(int count)
    {
        var probabilities = new ushort[count];
        Array.Fill(probabilities, ProbabilityInit);
        return probabilities;
    }

    private struct RangeDecoder
    {
        private uint _range;
        private uint _code;
        private int _pos;

        public RangeDecoder(ReadOnlySpan<byte> input)
        {
            if (input.Length < 5) throw new LzmaFormatException("Truncated LZMA stream.");
            if (input[0] != 0) throw new LzmaFormatException("LZMA stream does not start with a zero byte.");
            _range = uint.MaxValue;
            _pos = 1;
            for (int i = 0; i < 4; i++) _code = (_code << 8) | input[_pos++];
            if (_code == _range) throw new LzmaFormatException("Corrupt LZMA stream.");
        }

        // Reading past the end yields zeros so a stream whose last bits sit in
        // the final byte still decodes; a corrupt stream is caught by the size checks.
        private byte Next(ReadOnlySpan<byte> input)
        {
            int at = _pos++;
            return at < input.Length ? input[at] : (byte)0;
        }

        private void Normalize(ReadOnlySpan<byte> input)
        {
            if (_range >= 0x1000000) return;
            _range <<= 8;
            _code = (_code << 8) | Next(input);
        }

        public int Bit(ReadOnlySpan<byte> input, ushort[] probabilities, int index)
        {
            uint p = probabilities[index];
            uint bound = (_range >> 11) * p;
            int bit;
            if (_code < bound)
            {
                _range = bound;
                probabilities[index] = (ushort)(p + ((2048 - p) >> 5));
                bit = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probabilities[index] = (ushort)(p - (p >> 5));
                bit = 1;
            }

            Normalize(input);
            return bit;
        }

        public uint Direct(ReadOnlySpan<byte> input, int bits)
        {
            uint result = 0;
            for (; bits > 0; bits--)
            {
                _range >>= 1;
                uint code = _code - _range;
                uint mask = (code >> 31) != 0 ? uint.MaxValue : 0;
                _code = code + (_range & mask);
                result = (result << 1) + (mask != 0 ? 0u : 1u);
                Normalize(input);
            }

            return result;
        }

        public int Tree(ReadOnlySpan<byte> input, ushort[] probabilities, int first, int bits)
        {
            int m = 1;
            for (int i = 0; i < bits; i++) m = (m << 1) | Bit(input, probabilities, first + m);
            return m - (1 << bits);
        }

        public uint ReverseTree(ReadOnlySpan<byte> input, ushort[] probabilities, int first, int bits)
        {
            int m = 1;
            uint symbol = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = Bit(input, probabilities, first + m);
                m = (m << 1) | bit;
                symbol |= (uint)bit << i;
            }

            return symbol;
        }
    }

    private sealed class LengthDecoder
    {
        private readonly ushort[] _choice = Probabilities(2);
        private readonly ushort[] _low = Probabilities(PosStatesMax << 3);
        private readonly ushort[] _mid = Probabilities(PosStatesMax << 3);
        private readonly ushort[] _high = Probabilities(256);

        public int Decode(ref RangeDecoder range, ReadOnlySpan<byte> input, int posState)
        {
            if (range.Bit(input, _choice, 0) == 0) return range.Tree(input, _low, posState << 3, 3);
            if (range.Bit(input, _choice, 1) == 0) return 8 + range.Tree(input, _mid, posState << 3, 3);
            return 16 + range.Tree(input, _high, 0, 8);
        }
    }
}
