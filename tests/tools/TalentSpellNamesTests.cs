using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class TalentSpellNamesTests
{
    private static readonly IReadOnlyDictionary<uint, string> SpellStrings = new Dictionary<uint, string>
    {
        [500] = "",
        [501] = "Permanently increases the effect of Elemental by 30%.",
        [502] = "Ice Storm",
    };

    private static readonly IReadOnlyDictionary<uint, string> TalentStrings = new Dictionary<uint, string>
    {
        [10] = "Explosive",
        [11] = "Heart of Fire",
        [12] = "Fear of Flame",
        [13] = "Frozen Gale",
    };

    [Fact]
    public void ARankSpellWithNoNameOrASharedOne_TakesItsTalentsName_AndOneWithItsOwnKeepsIt()
    {
        var spellNameIds = new Dictionary<uint, uint>
        {
            [14261] = 500,
            [14262] = 500,
            [15056] = 501,
            [15058] = 501,
            [15331] = 502,
            [99] = 777,
        };

        var renamed = TalentSpellNames.Renamed(spellNameIds, SpellStrings,
        [
            new(192, 10, [14261, 14262]),
            new(300, 11, [15056]),
            new(301, 12, [15058]),
            new(302, 13, [15331]),
            new(303, 99, [99]),
        ], TalentStrings);

        Assert.Equal("Explosive", renamed[14261]);
        Assert.Equal("Explosive", renamed[14262]);
        Assert.Equal("Heart of Fire", renamed[15056]);
        Assert.Equal("Fear of Flame", renamed[15058]);
        Assert.False(renamed.ContainsKey(15331));
        Assert.False(renamed.ContainsKey(99));
    }
}
