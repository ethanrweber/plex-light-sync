using PlexLightSync.Color;
using PlexLightSync.Sync;

namespace PlexLightSync.Tests.Sync;

public sealed class ColorSchedulerTests
{
    static readonly ColorPick Red = ColorPick.Of(new HsColor(0, 1));
    static readonly ColorPick Blue = ColorPick.Of(new HsColor(240, 1));
    static readonly ColorPick Green = ColorPick.Of(new HsColor(120, 1));

    readonly ColorScheduler _scheduler = new();

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Nothing_is_due_before_its_time()
    {
        _scheduler.Add(S(10), Red);

        Assert.Null(_scheduler.TakeDue(S(9.9)));
        Assert.Equal(Red, _scheduler.TakeDue(S(10)));
    }

    [Fact]
    public void Of_several_due_picks_only_the_newest_comes_out()
    {
        _scheduler.Add(S(58), Red); // the keyframe before the start
        _scheduler.Add(S(59), Blue);
        _scheduler.Add(S(62), Green);

        Assert.Equal(Blue, _scheduler.TakeDue(S(60)));
        Assert.Null(_scheduler.TakeDue(S(61)));
        Assert.Equal(Green, _scheduler.TakeDue(S(62)));
    }

    [Fact]
    public void A_frame_that_arrives_after_a_newer_one_was_taken_is_dropped()
    {
        _scheduler.Add(S(10), Red);
        _scheduler.TakeDue(S(10));

        _scheduler.Add(S(4), Blue); // out of order

        Assert.Null(_scheduler.TakeDue(S(20)));
    }

    [Fact]
    public void The_horizon_is_the_newest_queued_timestamp()
    {
        Assert.Null(_scheduler.Horizon);

        _scheduler.Add(S(10), Red);
        _scheduler.Add(S(30), Blue);
        Assert.Equal(S(30), _scheduler.Horizon);

        _scheduler.TakeDue(S(40));
        Assert.Equal(S(30), _scheduler.Horizon);
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        _scheduler.Add(S(10), Red);
        _scheduler.TakeDue(S(10));
        _scheduler.Clear();

        _scheduler.Add(S(4), Blue); // after a rewind, an earlier time is fine again

        Assert.Equal(Blue, _scheduler.TakeDue(S(5)));
    }
}
