using Ubiquitous.Common;

namespace Ubiquitous.Common.Tests;

public class EntityIdTests
{
    [Fact]
    public void EntityId_PacksTheNumberAboveTheTypeByte()
    {
        var id = EntityId.From(ObjectType.Player, 42);

        Assert.Equal(42UL << 8 | (ulong)ObjectType.Player, id.Raw);
    }

    [Fact]
    public void EntityId_TypeAndLowValue_Roundtrip()
    {
        var id = EntityId.From(ObjectType.Creature, 0xDEADBEEF);

        Assert.Equal(ObjectType.Creature, id.Type);
        Assert.Equal(0xDEADBEEFu, id.LowValue);
    }

    [Fact]
    public void EntityId_ParsesWhatItWrites()
    {
        var id = EntityId.From(ObjectType.StaticObject, 4_000_000_000);

        Assert.Equal(id, EntityId.Parse(id.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Creature")]
    [InlineData("Creature:")]
    [InlineData(":12")]
    [InlineData("Dragon:12")]
    [InlineData("9:12")]
    [InlineData("Creature:-1")]
    [InlineData("Creature:1:2")]
    public void EntityId_RefusesAnythingElse(string text)
    {
        Assert.False(EntityId.TryParse(text, out _));
        Assert.Throws<FormatException>(() => EntityId.Parse(text));
    }
}

public class PositionTests
{
    [Fact]
    public void Position_DistanceSquared_UsesFloatMath()
    {
        var a = new Position(1f, 2f, 3f);
        var b = new Position(4f, 6f, 3f);

        Assert.Equal(25f, a.DistanceSquaredTo(b));
    }

}
