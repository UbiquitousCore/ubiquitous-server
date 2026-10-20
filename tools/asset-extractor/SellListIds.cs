namespace Ubiquitous.AssetExtractor;

public static class SellListIds
{
    // creature_proto column 55 lists a vendor's sell lists, one per shop tab,
    // each followed by a comma ("114,115,116,117," or "52,"); a few rows
    // carry one bare number.
    public static IReadOnlyList<uint> Parse(string text) =>
        [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => uint.TryParse(part, out var id) ? id : 0u)
            .Where(id => id != 0)];
}
