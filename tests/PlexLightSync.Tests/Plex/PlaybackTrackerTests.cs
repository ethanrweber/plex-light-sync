using Microsoft.Extensions.Time.Testing;
using PlexLightSync.Plex;

namespace PlexLightSync.Tests.Plex;

public sealed class PlaybackTrackerTests
{
    static readonly TimeSpan ReportDelay = TimeSpan.FromSeconds(0.5);
    static readonly PlayingItem Movie = new("100", 1);
    static readonly PlayingItem NextEpisode = new("101", 2);

    readonly FakeTimeProvider _time = new();
    readonly PlaybackTracker _tracker;

    public PlaybackTrackerTests() =>
        _tracker = new PlaybackTracker(_time, seekThreshold: TimeSpan.FromSeconds(3), ReportDelay);

    static PlayerReport Report(double seconds, PlayerState state = PlayerState.Playing, PlayingItem? item = null) =>
        new(item ?? Movie, TimeSpan.FromSeconds(seconds), state);

    static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void A_first_report_starts_playback_half_a_poll_after_the_reported_offset()
    {
        var started = Assert.IsType<PlaybackStarted>(_tracker.Observe(Report(60)));

        Assert.Equal(Movie, started.Item);
        Assert.Equal(Seconds(60.5), started.Position);
        Assert.True(started.IsPlaying);
    }

    [Fact]
    public void The_position_moves_with_the_clock_while_playing()
    {
        _tracker.Observe(Report(60));
        _time.Advance(Seconds(4));

        Assert.Equal(Seconds(64.5), _tracker.Position);
    }

    [Fact]
    public void The_same_report_again_is_no_event_and_the_clock_keeps_running()
    {
        _tracker.Observe(Report(60));
        _time.Advance(Seconds(1));

        Assert.Null(_tracker.Observe(Report(60)));
        Assert.Equal(Seconds(61.5), _tracker.Position);
    }

    [Fact]
    public void A_report_close_to_the_estimate_corrects_the_clock()
    {
        _tracker.Observe(Report(60));
        _time.Advance(Seconds(10));

        // the estimate is 70.5; the tv says 71 (plus the half-poll delay)
        var corrected = Assert.IsType<PositionCorrected>(_tracker.Observe(Report(71)));

        Assert.Equal(Seconds(71.5), corrected.Position);
        Assert.Equal(Seconds(1), corrected.Drift);
        Assert.Equal(Seconds(71.5), _tracker.Position);
    }

    [Theory]
    [InlineData(300)] // skip forward
    [InlineData(20)]  // rewind
    public void A_report_far_from_the_estimate_is_a_skip(double reported)
    {
        _tracker.Observe(Report(60));
        _time.Advance(Seconds(10));

        var seeked = Assert.IsType<PlaybackSeeked>(_tracker.Observe(Report(reported)));

        Assert.Equal(Seconds(reported + 0.5), seeked.Position);
        Assert.Equal(Seconds(reported - 70), seeked.Jump);
    }

    [Theory]
    [InlineData(PlayerState.Paused)]
    [InlineData(PlayerState.Buffering)]
    public void A_pause_or_buffering_stops_the_clock_at_the_reported_offset(PlayerState state)
    {
        _tracker.Observe(Report(60));
        _time.Advance(Seconds(5));

        var paused = Assert.IsType<PlaybackPaused>(_tracker.Observe(Report(65, state)));
        _time.Advance(Seconds(30));

        Assert.Equal(Seconds(65), paused.Position);
        Assert.Equal(state, paused.State);
        Assert.Equal(Seconds(65), _tracker.Position);
    }

    [Fact]
    public void A_resume_starts_the_clock_again()
    {
        _tracker.Observe(Report(60));
        _tracker.Observe(Report(65, PlayerState.Paused));
        _time.Advance(Seconds(30));

        var resumed = Assert.IsType<PlaybackResumed>(_tracker.Observe(Report(65)));
        _time.Advance(Seconds(2));

        Assert.Equal(Seconds(65.5), resumed.Position);
        Assert.Equal(Seconds(67.5), _tracker.Position);
    }

    [Fact]
    public void A_skip_during_a_pause_is_no_event_but_the_resume_uses_the_new_offset()
    {
        _tracker.Observe(Report(60));
        _tracker.Observe(Report(65, PlayerState.Paused));

        Assert.Null(_tracker.Observe(Report(600, PlayerState.Paused)));
        var resumed = Assert.IsType<PlaybackResumed>(_tracker.Observe(Report(600)));

        Assert.Equal(Seconds(600.5), resumed.Position);
    }

    [Fact]
    public void A_new_item_starts_again_without_a_stop()
    {
        _tracker.Observe(Report(1500));

        var started = Assert.IsType<PlaybackStarted>(_tracker.Observe(Report(0, item: NextEpisode)));

        Assert.Equal(NextEpisode, started.Item);
        Assert.Equal(Seconds(0.5), started.Position);
    }

    [Fact]
    public void An_item_that_starts_paused_has_a_still_clock()
    {
        var started = Assert.IsType<PlaybackStarted>(_tracker.Observe(Report(60, PlayerState.Paused)));
        _time.Advance(Seconds(10));

        Assert.False(started.IsPlaying);
        Assert.Equal(Seconds(60), _tracker.Position);
    }

    [Fact]
    public void No_session_stops_playback_once()
    {
        _tracker.Observe(Report(60));

        Assert.IsType<PlaybackStopped>(_tracker.Observe(null));
        Assert.Null(_tracker.Observe(null));
        Assert.Null(_tracker.Position);
        Assert.Null(_tracker.Item);
    }
}
