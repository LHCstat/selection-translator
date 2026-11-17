using SelectionTranslator.Core.Selection;

namespace SelectionTranslator.Tests.Selection;

public class DragTrackerTests
{
    [Fact]
    public void Click_is_not_drag()
    {
        var tracker = new DragTracker();
        tracker.OnDown(100, 100, 0);
        Assert.False(tracker.OnUp(101, 101, 200));
    }

    [Fact]
    public void Move_beyond_distance_is_drag()
    {
        var tracker = new DragTracker();
        tracker.OnDown(100, 100, 0);
        Assert.True(tracker.OnUp(120, 130, 200));
    }

    [Fact]
    public void Too_short_duration_is_not_drag()
    {
        var tracker = new DragTracker(minDuration: TimeSpan.FromMilliseconds(50));
        tracker.OnDown(100, 100, 0);
        Assert.False(tracker.OnUp(200, 200, 10));
    }

    [Fact]
    public void Up_without_down_is_ignored()
    {
        var tracker = new DragTracker();
        Assert.False(tracker.OnUp(200, 200, 100));
    }

    [Fact]
    public void Second_up_after_drag_is_ignored()
    {
        var tracker = new DragTracker();
        tracker.OnDown(0, 0, 0);
        Assert.True(tracker.OnUp(50, 50, 200));
        Assert.False(tracker.OnUp(60, 60, 300));
    }
}