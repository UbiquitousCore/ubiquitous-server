using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

[Collection(nameof(BasicSpellsTests))]
public sealed class BasicSpellsTests : IDisposable
{
    private static Dictionary<uint, SpellDefinition> Spells(params uint[] ids) =>
        ids.ToDictionary(id => id, id => new SpellDefinition(id, $"spell-{id}", 0, 0, 0f));

    public void Dispose() => BasicSpells.UseNumberingOf(Spells(108));

    [Fact]
    public void AStartingCharacterGetsTheLaterBuildsAutoAttackWhereTheEarlyOneIsGone()
    {
        BasicSpells.UseNumberingOf(Spells(1, 2, 3, 4, 5, 6, 7));

        var loadout = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Warrior)!;

        Assert.Contains(5u, loadout.Spells);
        Assert.DoesNotContain(108u, loadout.Spells);
        Assert.Equal(2u, MiningRules.GatherSpellId);
    }

    [Fact]
    public void ABuildWithTheEarlyAutoAttackKeepsTheEarlyNumbering()
    {
        BasicSpells.UseNumberingOf(Spells(5, 108));

        Assert.Equal(108u, BasicSpells.AutoAttack);
        Assert.Equal(23u, MiningRules.GatherSpellId);
    }
}
