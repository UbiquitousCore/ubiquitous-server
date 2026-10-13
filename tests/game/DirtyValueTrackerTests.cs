namespace Ubiquitous.Game.Tests;

public sealed class DirtyValueTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FlushDue_HoldsMarksInsideTheWindow()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("Creature:1", "health", 80, Now);

        Assert.Empty(tracker.FlushDue(Now + TimeSpan.FromMilliseconds(499)));
    }

    [Fact]
    public void FlushDue_CarriesAgedMarksOldestUnitFirst()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("player:x", "health", 47, Now);
        tracker.Mark("Creature:1", "health", 80, Now);

        var flushed = tracker.FlushDue(Now + TimeSpan.FromMilliseconds(500));

        Assert.Equal(2, flushed.Count);
        Assert.Equal("Creature:1", flushed[0].UnitId);
        Assert.Equal("player:x", flushed[1].UnitId);
        Assert.Equal(80, flushed[0].Changed["health"]);
    }

    [Fact]
    public void Mark_LastWriteWinsInsideOneWindow()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("Creature:1", "health", 90, Now);
        tracker.Mark("Creature:1", "health", 80, Now + TimeSpan.FromMilliseconds(100));

        var flushed = tracker.FlushDue(Now + TimeSpan.FromMilliseconds(600));

        var single = Assert.Single(flushed);
        Assert.Equal(80, single.Changed["health"]);
    }

    [Fact]
    public void Mark_SkipsAValueThatNeverMoved()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("Creature:1", "health", 83, Now);

        Assert.Single(tracker.FlushDue(Now + TimeSpan.FromSeconds(1)));

        tracker.Mark("Creature:1", "health", 83, Now + TimeSpan.FromSeconds(2));
        Assert.Empty(tracker.FlushDue(Now + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Clear_DropsPendingAndRememberedValues()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("Creature:1", "health", 80, Now);
        tracker.Clear();

        Assert.Empty(tracker.FlushDue(Now + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void MarkSent_DropsAStaleBatchedValueSoItCannotFlushAfterTheSend()
    {
        var tracker = new DirtyValueTracker();
        tracker.Mark("Creature:1", "health", 40, Now);
        tracker.Mark("Creature:1", "power", 5, Now);

        tracker.MarkSent("Creature:1", new Dictionary<string, int> { ["health"] = 100 });
        var flushed = tracker.FlushDue(Now + TimeSpan.FromSeconds(1));

        var single = Assert.Single(flushed);
        Assert.False(single.Changed.ContainsKey("health"));
        Assert.Equal(5, single.Changed["power"]);
    }

    [Fact]
    public void MarkSent_CountsAsSentSoTheSameValueIsNotSentAgain()
    {
        var tracker = new DirtyValueTracker();
        tracker.MarkSent("Creature:1", new Dictionary<string, int> { ["health"] = 100 });

        tracker.Mark("Creature:1", "health", 100, Now);

        Assert.Empty(tracker.FlushDue(Now + TimeSpan.FromSeconds(1)));
    }
}
