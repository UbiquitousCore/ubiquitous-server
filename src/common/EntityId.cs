namespace Ubiquitous.Common;

public readonly record struct EntityId(ulong Raw)
{
    public ObjectType Type => (ObjectType)(byte)Raw;

    public uint LowValue => unchecked((uint)(Raw >> 8));

    public static EntityId From(ObjectType type, uint lowValue) =>
        new(((ulong)lowValue << 8) | (byte)type);

    public override string ToString() => $"{Type}:{LowValue}";

    public static EntityId Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"'{text}' is not a Type:number entity id.");

    public static bool TryParse(ReadOnlySpan<char> text, out EntityId id)
    {
        id = default;
        var colon = text.IndexOf(':');
        if (colon <= 0
            || !Enum.TryParse<ObjectType>(text[..colon], out var type)
            || !Enum.IsDefined(type)
            || !uint.TryParse(text[(colon + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var lowValue))
            return false;
        id = From(type, lowValue);
        return true;
    }
}
