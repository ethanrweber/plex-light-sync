using Microsoft.Extensions.Options;
using PlexLightSync.Color;
using PlexLightSync.Video;

namespace PlexLightSync.Tests.Color;

public sealed class ColorPickerTests
{
    static readonly ColorPicker Picker = new(Options.Create(new ColorOptions()));

    // builds a 64×36 frame from a function of (x, y)
    static VideoFrame Frame(Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var bytes = new byte[VideoFrame.ByteCount];
        for (var y = 0; y < VideoFrame.Height; y++)
        for (var x = 0; x < VideoFrame.Width; x++)
        {
            var (r, g, b) = pixel(x, y);
            var i = 3 * (y * VideoFrame.Width + x);
            (bytes[i], bytes[i + 1], bytes[i + 2]) = (r, g, b);
        }
        return new VideoFrame(TimeSpan.Zero, bytes);
    }

    static VideoFrame Solid(byte r, byte g, byte b) => Frame((_, _) => (r, g, b));

    static HsColor ColorOf(VideoFrame frame)
    {
        var pick = Picker.Pick(frame);
        Assert.Equal(HoldReason.None, pick.Reason);
        return pick.Color!.Value;
    }

    static void AssertHue(double expected, double actual, double tolerance = 1)
    {
        var distance = Math.Abs(expected - actual) % 360;
        Assert.True(Math.Min(distance, 360 - distance) <= tolerance, $"hue {actual:0.0}°, expected {expected:0.0}°");
    }

    [Fact]
    public void A_solid_color_comes_out_as_that_hue_at_full_saturation()
    {
        var color = ColorOf(Solid(255, 0, 0));

        AssertHue(0, color.Hue);
        Assert.Equal(1, color.Saturation);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 3, 2)] // noise just above black
    public void A_black_frame_holds(byte r, byte g, byte b) =>
        Assert.Equal(ColorPick.Black, Picker.Pick(Solid(r, g, b)));

    [Theory]
    [InlineData(128, 128, 128)] // gray
    [InlineData(150, 170, 180)] // almost gray: saturation 0.17
    public void A_frame_without_color_holds(byte r, byte g, byte b) =>
        Assert.Equal(ColorPick.NoColor, Picker.Pick(Solid(r, g, b)));

    [Fact]
    public void Two_colors_do_not_average_to_gray_the_stronger_one_wins()
    {
        // 38 of 64 columns teal, the rest orange
        var color = ColorOf(Frame((x, _) => x < 38 ? ((byte)0, (byte)200, (byte)200) : ((byte)200, (byte)100, (byte)0)));

        AssertHue(180, color.Hue);
    }

    [Fact]
    public void Letterbox_bars_are_ignored()
    {
        var color = ColorOf(Frame((_, y) => y is < 6 or >= 30 ? ((byte)0, (byte)0, (byte)0) : ((byte)30, (byte)60, (byte)200)));

        AssertHue(229.4, color.Hue);
    }

    [Fact]
    public void A_dark_blue_night_scene_is_judged_against_its_own_light()
    {
        var color = ColorOf(Solid(0, 0, 40));

        AssertHue(240, color.Hue);
        Assert.Equal(1, color.Saturation);
    }

    [Fact]
    public void A_small_red_light_in_a_dark_room_shows()
    {
        // 184 pixels (8%) of dim red; the rest is near-black gray
        var color = ColorOf(Frame((x, y) => x < 8 && y < 23 ? ((byte)60, (byte)5, (byte)5) : ((byte)10, (byte)10, (byte)12)));

        AssertHue(0, color.Hue);
    }

    [Fact]
    public void A_speck_of_color_is_not_enough()
    {
        // 10 red pixels: less than 1% of the frame
        var frame = Frame((x, y) => y == 0 && x < 10 ? ((byte)255, (byte)0, (byte)0) : ((byte)128, (byte)128, (byte)128));

        Assert.Equal(ColorPick.NoColor, Picker.Pick(frame));
    }

    [Fact]
    public void A_small_but_clear_patch_of_color_is_enough()
    {
        // 50 red pixels: 2% of the frame
        var frame = Frame((x, y) => y < 5 && x < 10 ? ((byte)255, (byte)0, (byte)0) : ((byte)128, (byte)128, (byte)128));

        AssertHue(0, ColorOf(frame).Hue);
    }

    [Fact]
    public void Saturation_gets_the_boost()
    {
        // hue 200°, saturation 0.33
        var color = ColorOf(Solid(120, 160, 180));

        AssertHue(200, color.Hue);
        Assert.Equal(0.5, color.Saturation, precision: 3);
    }

    [Fact]
    public void Hues_on_both_sides_of_red_average_to_red()
    {
        // 355° and 5°: a plain average gives 180° (cyan)
        var color = ColorOf(Frame((x, _) => x < 32 ? ((byte)255, (byte)0, (byte)20) : ((byte)255, (byte)20, (byte)0)));

        AssertHue(0, color.Hue);
    }
}
