using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class TeleportServiceTests
{
    private static readonly Dictionary<uint, NpcDefinition> Npcs = new()
    {
        [9002] = new(9002, NpcFlags.Teleporter),
        [9001] = new(9001, NpcFlags.Vendor, OffersQuests: true),
    };

    [Fact]
    public void Request_RefusesABodyThePlayerCannotSee()
    {
        var result = new TeleportService().Request("Creature:7",
            new Dictionary<string, uint>(), Npcs);

        Assert.False(result.Ok);
        Assert.Equal(TeleportRefusal.NotVisible, result.Refusal);
        Assert.Equal("Creature:7", result.NetworkId);
    }

    [Fact]
    public void Request_RefusesAVisibleBodyWithNoNpcOverlay()
    {
        var result = new TeleportService().Request("Creature:1",
            new Dictionary<string, uint> { ["Creature:1"] = 4242 }, Npcs);

        Assert.False(result.Ok);
        Assert.Equal(TeleportRefusal.UnknownNpc, result.Refusal);
    }

    [Fact]
    public void Request_RefusesAnNpcWithoutTheTeleporterFlag()
    {
        var result = new TeleportService().Request("Creature:1",
            new Dictionary<string, uint> { ["Creature:1"] = 9001 }, Npcs);

        Assert.False(result.Ok);
        Assert.Equal(TeleportRefusal.NotTeleporter, result.Refusal);
    }

    [Fact]
    public void Request_AcceptsAnyVisibleTeleporter()
    {
        var result = new TeleportService().Request("Creature:1",
            new Dictionary<string, uint> { ["Creature:1"] = 9002 }, Npcs);

        Assert.True(result.Ok);
        Assert.Equal(TeleportRefusal.None, result.Refusal);
    }
}
