namespace Ubiquitous.Common;

public readonly record struct MapId(ushort Value)
{
    public override string ToString() => Value.ToString();
}
