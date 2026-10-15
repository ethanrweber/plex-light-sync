using PlexLightSync.Color;
using PlexLightSync.Sync;

namespace PlexLightSync.Tests.Sync;

public sealed class ColorSenderTests
{
    static readonly HsColor Red = new(0, 1);
    static readonly HsColor Blue = new(240, 1);
    static readonly HsColor Green = new(120, 1);

    readonly ColorSender _sender = new(minGap: TimeSpan.FromSeconds(2));

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void The_first_color_goes_out_at_once()
    {
        _sender.Offer(Red);

        Assert.Equal(Red, _sender.TakeReady(S(0)));
        Assert.Null(_sender.TakeReady(S(0.1)));
    }

    [Fact]
    public void A_color_waits_for_the_gap_and_the_newest_wins()
    {
        _sender.Offer(Red);
        _sender.TakeReady(S(0));

        _sender.Offer(Blue);
        Assert.Null(_sender.TakeReady(S(1)));
        _sender.Offer(Green);

        Assert.Equal(Green, _sender.TakeReady(S(2)));
    }

    [Fact]
    public void An_almost_identical_color_is_dropped()
    {
        _sender.Offer(Red);
        _sender.TakeReady(S(0));

        _sender.Offer(new HsColor(4, 0.97));

        Assert.Null(_sender.TakeReady(S(5)));
    }

    [Fact]
    public void Cancel_drops_the_waiting_color()
    {
        _sender.Offer(Red);
        _sender.TakeReady(S(0));
        _sender.Offer(Blue);

        _sender.Cancel();

        Assert.Null(_sender.TakeReady(S(5)));
    }

    [Fact]
    public void After_a_reset_the_same_color_goes_out_again()
    {
        _sender.Offer(Red);
        _sender.TakeReady(S(0));

        _sender.Reset();
        _sender.Offer(Red);

        Assert.Equal(Red, _sender.TakeReady(S(5)));
    }

    [Theory]
    [InlineData(0, 1, 7, 1, true)]       // 7° apart
    [InlineData(356, 1, 3, 1, true)]     // 7° apart, across 0°
    [InlineData(355, 1, 3, 1, false)]    // 8° apart: the limit
    [InlineData(0, 1, 20, 1, false)]
    [InlineData(0, 1, 0, 0.8, false)]    // same hue, clearly paler
    [InlineData(0, 0.1, 180, 0.12, true)] // both nearly white: the hue hardly shows
    public void Similarity(double hueA, double satA, double hueB, double satB, bool similar) =>
        Assert.Equal(similar, ColorSender.IsSimilar(new HsColor(hueA, satA), new HsColor(hueB, satB)));
}
