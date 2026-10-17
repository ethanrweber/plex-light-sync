using PlexLightSync.Color;
using PlexLightSync.Sync;

namespace PlexLightSync.Tests.Sync;

public sealed class ColorVoteTests
{
    static readonly HsColor Red = new(0, 0.8);
    static readonly HsColor Blue = new(240, 0.8);
    static readonly HsColor Green = new(120, 0.8);

    static ColorPick[] Picks(params HsColor?[] colors) =>
        [.. colors.Select(color => color is { } known ? ColorPick.Of(known) : ColorPick.Black)];

    static ColorPick[] Repeat(HsColor color, int count) => Picks([.. Enumerable.Repeat<HsColor?>(color, count)]);

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Black_and_colorless_frames_hold()
    {
        Assert.Null(ColorVote.Decide(Picks(null, null), Red));
        Assert.Null(ColorVote.Decide([ColorPick.NoColor], current: null));
    }

    [Fact]
    public void Without_a_current_color_the_winner_goes_out()
    {
        var color = ColorVote.Decide([.. Repeat(Blue, 3), .. Repeat(Red, 1)], current: null);

        Assert.Equal(240, color!.Value.Hue, precision: 1);
    }

    [Fact]
    public void A_winner_close_to_the_current_color_changes_nothing()
    {
        Assert.Null(ColorVote.Decide(Repeat(new HsColor(15, 0.7), 5), Red));
    }

    [Fact]
    public void A_clearly_paler_winner_of_the_same_hue_goes_out()
    {
        var color = ColorVote.Decide(Repeat(new HsColor(0, 0.4), 5), Red);

        Assert.Equal(0.4, color!.Value.Saturation, precision: 2);
    }

    [Fact]
    public void A_new_color_needs_one_and_a_half_times_the_votes_of_the_current_color()
    {
        Assert.Null(ColorVote.Decide([.. Repeat(Red, 4), .. Repeat(Blue, 5)], Red));
        Assert.Equal(Blue.Hue, ColorVote.Decide([.. Repeat(Red, 4), .. Repeat(Blue, 6)], Red)!.Value.Hue, precision: 1);
    }

    [Fact]
    public void A_short_flash_in_a_scene_does_not_win()
    {
        // a ±2 s window at 2 frames per second: 9 frames, 3 of them (1.5 s) a flash
        Assert.Null(ColorVote.Decide([.. Repeat(Green, 3), .. Repeat(Blue, 3), .. Repeat(Green, 3)], Green));
    }

    [Fact]
    public void The_winner_is_the_mean_of_its_frames_across_0_degrees()
    {
        var color = ColorVote.Decide(Picks(new HsColor(355, 0.6), new HsColor(5, 0.8)), current: null);

        Assert.True(color!.Value.Hue is < 0.5 or > 359.5, $"hue {color.Value.Hue}");
        Assert.Equal(0.7, color.Value.Saturation, precision: 2);
    }

    [Fact]
    public void With_keyframes_only_the_newest_frame_decides_alone()
    {
        var timeline = new ColorTimeline();
        timeline.Add(S(50), ColorPick.Of(Green));
        timeline.Add(S(58), ColorPick.Of(Blue));
        timeline.Add(S(62), ColorPick.Of(Green));

        Assert.Equal(Blue.Hue, ColorVote.Decide(timeline, S(60), S(2), keyframesOnly: true, Red)!.Value.Hue, precision: 1);
    }

    [Fact]
    public void The_window_reaches_before_and_after_the_moment()
    {
        var timeline = new ColorTimeline();
        for (var t = 50.0; t < 70; t += 0.5)
            timeline.Add(S(t), ColorPick.Of(t < 60 ? Red : Blue));

        Assert.Null(ColorVote.Decide(timeline, S(59.5), S(2), keyframesOnly: false, Red));          // 5 red, 4 blue
        Assert.NotNull(ColorVote.Decide(timeline, S(60.5), S(2), keyframesOnly: false, Red));       // 3 red, 6 blue
    }
}
