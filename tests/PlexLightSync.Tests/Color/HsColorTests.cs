using PlexLightSync.Color;

namespace PlexLightSync.Tests.Color;

public sealed class HsColorTests
{
    [Theory]
    [InlineData(0, 1, 255, 0, 0)]
    [InlineData(120, 1, 0, 255, 0)]
    [InlineData(240, 0.5, 128, 128, 255)]
    [InlineData(60, 0, 255, 255, 255)]
    [InlineData(300, 1, 255, 0, 255)]
    public void A_color_converts_to_rgb_at_full_brightness(double hue, double saturation, int r, int g, int b) =>
        Assert.Equal(((byte)r, (byte)g, (byte)b), new HsColor(hue, saturation).ToRgb());
}
