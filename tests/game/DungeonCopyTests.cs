using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class DungeonCopyTests
{
    private static readonly DungeonDefinition[] Dungeons =
    [
        new(14, "The Prison", [9401]),
        new(14, "The Prison", [2666], new LegendCounterparts(
            new Dictionary<uint, uint> { [2666] = 2669 },
            new Dictionary<uint, uint> { [2683] = 2764 })),
        new(20, "Elsewhere", [5000]),
    ];

    [Fact]
    public void ALegendCopy_HoldsEachCreatureAndSpellsLegendCounterpart()
    {
        var copy = DungeonCopy.For(Dungeons, 14, DungeonMode.Legend);

        Assert.Equal(DungeonMode.Legend, copy.Mode);
        Assert.Equal(2669u, copy.CreatureEntry(2666));
        Assert.Equal(2764u, copy.Spell(2683));
        Assert.Equal(2669u, copy.Spawn(new CreatureSpawnDefinition(1, 14, 2666, 0, 0, 0, 0)).CreatureEntryId);
    }

    [Fact]
    public void ALegendCopy_KeepsWhatHasNoCounterpart()
    {
        var copy = DungeonCopy.For(Dungeons, 14, DungeonMode.Legend);

        Assert.Equal(9401u, copy.CreatureEntry(9401));
        Assert.Equal(5233u, copy.Spell(5233));
    }

    [Fact]
    public void ALegendCreature_IsCountedAsTheCreatureTheDungeonNames()
    {
        var copy = DungeonCopy.For(Dungeons, 14, DungeonMode.Legend);

        Assert.Equal(2666u, copy.AuthoredEntry(2669));
        Assert.Equal(9401u, copy.AuthoredEntry(9401));
    }

    [Fact]
    public void ANormalCopy_ChangesNothing()
    {
        var copy = DungeonCopy.For(Dungeons, 14, DungeonMode.Normal);

        Assert.Same(DungeonCopy.Normal, copy);
        Assert.Equal(2666u, copy.CreatureEntry(2666));
        Assert.Equal(2683u, copy.Spell(2683));
    }

    [Fact]
    public void ADungeonWithNoLegendVersion_StaysNormalInLegendMode()
    {
        var copy = DungeonCopy.For(Dungeons, 20, DungeonMode.Legend);

        Assert.Same(DungeonCopy.Normal, copy);
        Assert.False(DungeonCopy.OffersLegend(Dungeons, 20));
        Assert.True(DungeonCopy.OffersLegend(Dungeons, 14));
    }
}
