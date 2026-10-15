using Microsoft.Extensions.Options;
using PlexLightSync.Video;

namespace PlexLightSync.Color;

/// <summary>Picks one vivid color from a frame, or a reason for the lights to hold their color.</summary>
/// <remarks>
/// <list type="number">
/// <item>Find the bright level of the frame: the 95th percentile of pixel brightness. Below the black
/// level, the frame counts as black.</item>
/// <item>Ignore the pixels darker than a quarter of the bright level (letterbox bars, deep shadows), and
/// the gray pixels. So a dark frame is judged against its own brightest pixels.</item>
/// <item>The remaining pixels vote for their hue, weighted by their chroma (how colorful they are). The
/// vote is by hue, not by rgb: the average of teal and orange is gray, but a vote picks one of them.</item>
/// <item>The winner is the 30° hue window with the most votes. The color is the mean of the pixels in
/// it, with a saturation boost.</item>
/// </list>
/// </remarks>
public sealed class ColorPicker(IOptions<ColorOptions> options)
{
    const int Bins = 36;               // 10° per bin; a window is a bin and its two neighbors
    const double DarkFraction = 0.25;  // pixels below this share of the bright level do not vote
    const double MinChroma = 0.02;     // ignores compression noise in near-black pixels
    const double MinCoverage = 0.01;   // the winning window needs at least 1% of the pixels

    public ColorPick Pick(VideoFrame frame)
    {
        var settings = options.Value;
        var pixels = ToHsv(frame.Pixels);

        var brightLevel = Percentile(pixels.Select(pixel => pixel.Value), 0.95);
        if (brightLevel < settings.BlackLevel)
            return ColorPick.Black;

        var darkLimit = brightLevel * DarkFraction;
        var voters = pixels
            .Where(pixel => pixel.Value >= darkLimit && pixel.Saturation >= settings.MinSaturation && pixel.Chroma >= MinChroma)
            .ToList();

        var votes = new double[Bins];
        foreach (var voter in voters)
            votes[Bin(voter.Hue)] += voter.Chroma;

        var winner = Enumerable.Range(0, Bins).MaxBy(bin => votes[Wrap(bin - 1)] + votes[bin] + votes[Wrap(bin + 1)]);
        var members = voters.Where(voter => CircularDistance(Bin(voter.Hue), winner) <= 1).ToList();
        if (members.Count < pixels.Length * MinCoverage)
            return ColorPick.NoColor;

        // a weighted circular mean: 350° and 10° average to 0°, not 180°
        double x = 0, y = 0, saturation = 0, weight = 0;
        foreach (var member in members)
        {
            var radians = member.Hue * Math.PI / 180;
            x += Math.Cos(radians) * member.Chroma;
            y += Math.Sin(radians) * member.Chroma;
            saturation += member.Saturation * member.Chroma;
            weight += member.Chroma;
        }

        var hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        return ColorPick.Of(new HsColor(hue, Math.Min(1, saturation / weight * settings.SaturationBoost)));
    }

    readonly record struct Hsv(double Hue, double Saturation, double Value, double Chroma);

    static Hsv[] ToHsv(byte[] rgb)
    {
        var pixels = new Hsv[rgb.Length / 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            double r = rgb[3 * i] / 255.0, g = rgb[3 * i + 1] / 255.0, b = rgb[3 * i + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), chroma = max - min;
            var hue = chroma == 0 ? 0
                : max == r ? 60 * ((g - b) / chroma % 6)
                : max == g ? 60 * ((b - r) / chroma + 2)
                : 60 * ((r - g) / chroma + 4);
            pixels[i] = new Hsv((hue + 360) % 360, max == 0 ? 0 : chroma / max, max, chroma);
        }
        return pixels;
    }

    static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Max(0, (int)Math.Ceiling(fraction * sorted.Length) - 1)];
    }

    static int Bin(double hue) => (int)(hue / (360.0 / Bins)) % Bins;

    static int Wrap(int bin) => (bin + Bins) % Bins;

    static int CircularDistance(int a, int b) => Math.Min(Wrap(a - b), Wrap(b - a));
}
