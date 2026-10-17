using PlexLightSync.Color;
using PlexLightSync.Sync;

namespace PlexLightSync.Tests.Sync;

public sealed class ColorTimelineTests
{
    static readonly ColorPick Red = ColorPick.Of(new HsColor(0, 1));
    static readonly ColorPick Blue = ColorPick.Of(new HsColor(240, 1));
    static readonly ColorPick Green = ColorPick.Of(new HsColor(120, 1));

    readonly ColorTimeline _timeline = new();

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Between_gives_the_picks_of_a_time_range_in_time_order()
    {
        _timeline.Add(S(12), Green); // out of order
        _timeline.Add(S(10), Red);
        _timeline.Add(S(11), Blue);
        _timeline.Add(S(13), Red);

        Assert.Equal([Red, Blue, Green], _timeline.Between(S(10), S(12)));
    }

    [Fact]
    public void Latest_is_the_newest_pick_at_or_before_a_time()
    {
        _timeline.Add(S(58), Red); // the keyframe before the start
        _timeline.Add(S(62), Blue);

        Assert.Null(_timeline.Latest(S(57)));
        Assert.Equal(Red, _timeline.Latest(S(60)));
        Assert.Equal(Blue, _timeline.Latest(S(62)));
    }

    [Fact]
    public void DropBefore_keeps_the_newest_older_pick_for_keyframes()
    {
        _timeline.Add(S(50), Green);
        _timeline.Add(S(58), Red);
        _timeline.Add(S(62), Blue);

        _timeline.DropBefore(S(60));

        Assert.Empty(_timeline.Between(S(0), S(57)));
        Assert.Equal(Red, _timeline.Latest(S(60)));
    }

    [Fact]
    public void The_horizon_is_the_newest_timestamp_that_arrived()
    {
        Assert.Null(_timeline.Horizon);

        _timeline.Add(S(30), Blue);
        _timeline.Add(S(10), Red);
        _timeline.DropBefore(S(40));
        Assert.Equal(S(30), _timeline.Horizon);

        _timeline.Clear();
        Assert.Null(_timeline.Horizon);
        Assert.Null(_timeline.Latest(S(100)));
    }
}
