using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class NpcOverlays
{
    public static IReadOnlyList<NpcDefinition> Build(
        IEnumerable<CreatureDefinition> creatures,
        IReadOnlySet<uint> questGivers,
        IReadOnlyDictionary<uint, uint> greetingByEntry)
    {
        var flagsByEntry = new Dictionary<uint, NpcFlags>();
        foreach (var creature in creatures.Where(creature => creature.RawNpcFlags != 0))
        {
            if (!flagsByEntry.TryAdd(creature.EntryId, (NpcFlags)creature.RawNpcFlags))
                throw new FTableFormatException($"Duplicate NPC entry {creature.EntryId}.");
        }

        foreach (var giver in questGivers) flagsByEntry.TryAdd(giver, NpcFlags.None);

        return [.. flagsByEntry.Select(pair => new NpcDefinition(
            pair.Key, pair.Value, greetingByEntry.GetValueOrDefault(pair.Key), questGivers.Contains(pair.Key)))];
    }
}
