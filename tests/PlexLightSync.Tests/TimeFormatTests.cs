namespace PlexLightSync.Tests;

public sealed class TimeFormatTests
{
    [Theory]
    [InlineData(3723.4, "1:02:03.4")]
    [InlineData(5.25, "0:00:05.2")]
    [InlineData(-0.2, "-0:00:00.2")]
    public void Positions_look_like_a_player_clock(double seconds, string expected) =>
        Assert.Equal(expected, TimeSpan.FromSeconds(seconds).ToClock());

    [Theory]
    [InlineData(0.4, "+0.4 s")]
    [InlineData(-12, "-12.0 s")]
    public void Differences_carry_their_sign(double seconds, string expected) =>
        Assert.Equal(expected, TimeSpan.FromSeconds(seconds).ToSignedSeconds());
}
