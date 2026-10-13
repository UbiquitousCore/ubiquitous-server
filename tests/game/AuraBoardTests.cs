namespace Ubiquitous.Game.Tests;

public sealed class AuraBoardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly object Ayla = new();
    private static readonly object Borin = new();

    private static HeldAura Slow(string unit = "Creature:1", int remainingMs = 10000, int wholeMs = 10000, string caster = "player:a") =>
        new(unit, caster, 7301, remainingMs, wholeMs, Harmful: true);

    [Fact]
    public void On_GivesWhatIsLeftOfAnAuraByNow()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);

        var held = Assert.Single(board.On("Creature:1", Now + TimeSpan.FromMilliseconds(2500)));
        Assert.Equal(7500, held.RemainingMs);
        Assert.Equal(10000, held.MaxDurationMs);
        Assert.Equal("player:a", held.CasterId);
    }

    [Fact]
    public void On_LeavesOutAnAuraWhoseTimeHasRunOut()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);

        Assert.Empty(board.On("Creature:1", Now + TimeSpan.FromMilliseconds(10000)));
    }

    [Fact]
    public void On_CountsDownFromTheWholeRunWhenNoTimeLeftWasSaid()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(remainingMs: 0, wholeMs: 4000), Now);

        Assert.Equal(3000, Assert.Single(board.On("Creature:1", Now + TimeSpan.FromSeconds(1))).RemainingMs);
    }

    [Fact]
    public void On_KeepsAnAuraThatLastsUntilTakenOff()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(remainingMs: 0, wholeMs: 0), Now);

        Assert.Equal(0, Assert.Single(board.On("Creature:1", Now + TimeSpan.FromHours(3))).RemainingMs);
    }

    [Fact]
    public void Applied_ASecondTimeRestartsTheSameAuraRatherThanAddingOne()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Ayla, Slow(), Now + TimeSpan.FromSeconds(5));

        Assert.Equal(9000, Assert.Single(board.On("Creature:1", Now + TimeSpan.FromSeconds(6))).RemainingMs);
    }

    [Fact]
    public void Ended_TakesOffOnlyWhatThatSessionReported()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Borin, Slow(caster: "player:b") with { SpellId = 7302 }, Now);

        board.Ended(Ayla, "Creature:1", 7302);
        Assert.Equal(2, board.On("Creature:1", Now).Count);

        board.Ended(Ayla, "Creature:1", 7301);
        Assert.Equal(7302u, Assert.Single(board.On("Creature:1", Now)).SpellId);
    }

    [Fact]
    public void Ended_WithSpellNoughtTakesOffEveryAuraThatSessionReportedOnTheBody()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Ayla, Slow() with { SpellId = 7302 }, Now);

        board.Ended(Ayla, "Creature:1", 0);

        Assert.Empty(board.On("Creature:1", Now));
    }

    [Fact]
    public void On_GivesOneAuraPerSpell_TheOneWithTheMostTimeLeft()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Borin, Slow(caster: "player:b"), Now + TimeSpan.FromSeconds(4));

        var held = Assert.Single(board.On("Creature:1", Now + TimeSpan.FromSeconds(5)));
        Assert.Equal("player:b", held.CasterId);
        Assert.Equal(9000, held.RemainingMs);
    }

    [Fact]
    public void Died_TakesOffEveryAuraOnTheBodyWhoeverReportedIt()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Borin, Slow(caster: "player:b") with { SpellId = 7302 }, Now);
        board.Applied(Ayla, Slow(unit: "Creature:2"), Now);

        board.Died("Creature:1");

        Assert.Empty(board.On("Creature:1", Now));
        Assert.Single(board.On("Creature:2", Now));
    }

    [Fact]
    public void Forget_DropsOneSessionsAurasOnOneBody_OrAllOfThem()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow(), Now);
        board.Applied(Ayla, Slow(unit: "Creature:2"), Now);
        board.Applied(Borin, Slow(unit: "Creature:2", caster: "player:b") with { SpellId = 7302 }, Now);

        board.Forget(Ayla, "Creature:1");
        Assert.Empty(board.On("Creature:1", Now));
        Assert.Equal(2, board.On("Creature:2", Now).Count);

        board.Forget(Ayla);
        Assert.Equal(7302u, Assert.Single(board.On("Creature:2", Now)).SpellId);
    }

    [Fact]
    public void On_KeepsTheOrderTheAurasArrivedIn()
    {
        var board = new AuraBoard();
        board.Applied(Ayla, Slow() with { SpellId = 3 }, Now);
        board.Applied(Ayla, Slow() with { SpellId = 1 }, Now);
        board.Applied(Ayla, Slow() with { SpellId = 2 }, Now);

        Assert.Equal([3u, 1u, 2u], board.On("Creature:1", Now).Select(aura => aura.SpellId));
    }
}
