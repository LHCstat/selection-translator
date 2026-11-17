namespace SelectionTranslator.Core.Selection;

public sealed class DragTracker
{
    private readonly int _minDistance;
    private readonly TimeSpan _minDuration;
    private (int X, int Y, long Ms)? _down;

    public DragTracker(int minDistance = 4, TimeSpan? minDuration = null)
    {
        _minDistance = minDistance;
        _minDuration = minDuration ?? TimeSpan.FromMilliseconds(50);
    }

    public void OnDown(int x, int y, long timestampMs) => _down = (x, y, timestampMs);

    public bool OnUp(int x, int y, long timestampMs)
    {
        if (_down is not { } down)
            return false;

        _down = null;

        var dx = x - down.X;
        var dy = y - down.Y;
        var farEnough = dx * dx + dy * dy >= _minDistance * _minDistance;
        var longEnough = timestampMs - down.Ms >= _minDuration.TotalMilliseconds;

        return farEnough && longEnough;
    }
}