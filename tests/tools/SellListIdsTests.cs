using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class SellListIdsTests
{
    [Fact]
    public void Parse_ReadsEveryListOfAMultiTabVendor() =>
        Assert.Equal([114u, 115u, 116u, 117u], SellListIds.Parse("114,115,116,117,"));

    [Fact]
    public void Parse_ReadsOneListFollowedByAComma() =>
        Assert.Equal([52u], SellListIds.Parse("52,"));

    [Fact]
    public void Parse_ReadsABareNumber() =>
        Assert.Equal([1u], SellListIds.Parse("1"));

    [Fact]
    public void Parse_FindsNoListInZeroOrBlank()
    {
        Assert.Empty(SellListIds.Parse("0"));
        Assert.Empty(SellListIds.Parse(""));
    }
}
