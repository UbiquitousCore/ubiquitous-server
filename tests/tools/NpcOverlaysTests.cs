using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public sealed class NpcOverlaysTests
{
    private static CreatureDefinition Creature(uint entry, uint npcFlags) =>
        new(entry, $"creature-{entry}", 1, 100, RawNpcFlags: npcFlags);

    [Fact]
    public void Build_KeepsEveryCreatureWithNpcFlagsAndPassesUnnamedBitsThrough()
    {
        var npcs = NpcOverlays.Build(
            [Creature(9001, (uint)NpcFlags.Gossip), Creature(9002, 0x80), Creature(9003, 0)],
            new HashSet<uint>(),
            new Dictionary<uint, uint>());

        Assert.Equal([9001u, 9002u], npcs.Select(npc => npc.EntryId));
        Assert.Equal(NpcFlags.Gossip, npcs[0].Flags);
        Assert.Equal((NpcFlags)0x80, npcs[1].Flags);
    }

    [Fact]
    public void Build_AQuestGiverWithNoFlags_IsStillAnNpcThatOffersQuests()
    {
        var npc = Assert.Single(NpcOverlays.Build(
            [Creature(9003, 0)], new HashSet<uint> { 9003 }, new Dictionary<uint, uint>()));

        Assert.Equal(new NpcDefinition(9003, NpcFlags.None, OffersQuests: true), npc);
    }

    [Fact]
    public void Build_CarriesTheGreetingPage()
    {
        var npc = Assert.Single(NpcOverlays.Build(
            [Creature(9001, (uint)NpcFlags.Gossip)], new HashSet<uint>(), new Dictionary<uint, uint> { [9001] = 44 }));

        Assert.Equal(44u, npc.GreetingMenuId);
    }

    [Fact]
    public void Build_RejectsTheSameCreatureEntryTwice()
    {
        var error = Assert.Throws<FTableFormatException>(() => NpcOverlays.Build(
            [Creature(9001, 1), Creature(9001, 2)], new HashSet<uint>(), new Dictionary<uint, uint>()));

        Assert.Contains("Duplicate NPC entry 9001", error.Message);
    }
}
