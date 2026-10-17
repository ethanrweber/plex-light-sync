using PlexLightSync.Color;

namespace PlexLightSync.Sync;

/// <summary>
/// Decides the color of the lights from the frames around a moment, so that the lights follow scenes,
/// not single frames. docs/plan.md explains the rules.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The frames in the window vote by hue: a frame is one vote. Black and colorless frames do not
/// vote. The 30° hue window with the most votes wins, and the color is the mean of its frames.</item>
/// <item>The current color keeps the lights unless the winner is clearly different (more than
/// <see cref="SameHue"/> of hue, or <see cref="SameSaturation"/> of saturation) and has at least
/// <see cref="Margin"/> times the votes of the current color.</item>
/// </list>
/// So a color that lasts less than half the window never wins, and two shots that alternate do not
/// make the lights alternate.
/// </remarks>
public static class ColorVote
{
    const int Bins = 36;                  // 10° per bin; a hue window is a bin and its two neighbors
    public const double SameHue = 20;     // degrees
    public const double SameSaturation = 0.25;
    public const double Margin = 1.5;

    /// <summary>
    /// The color for the lights at <paramref name="at"/>, or null to keep the current one. With
    /// <paramref name="keyframesOnly"/>, the newest frame decides alone: keyframes are seconds apart.
    /// </summary>
    /// <param name="window">How far the window reaches before and after <paramref name="at"/>.</param>
    public static HsColor? Decide(ColorTimeline timeline, TimeSpan at, TimeSpan window, bool keyframesOnly, HsColor? current)
    {
        IReadOnlyList<ColorPick> picks = keyframesOnly
            ? timeline.Latest(at) is { } latest ? [latest] : []
            : timeline.Between(at - window, at + window);
        return Decide(picks, current);
    }

    public static HsColor? Decide(IReadOnlyList<ColorPick> picks, HsColor? current)
    {
        var colors = picks.Select(pick => pick.Color).OfType<HsColor>().ToList();
        if (colors.Count == 0)
            return null; // black or colorless: hold

        var votes = new int[Bins];
        foreach (var color in colors)
            votes[Bin(color.Hue)]++;
        var winner = Enumerable.Range(0, Bins).MaxBy(bin => WindowVotes(votes, bin));
        var members = colors.Where(color => CircularDistance(Bin(color.Hue), winner) <= 1).ToList();
        var candidate = Mean(members);

        if (current is not { } now)
            return candidate;
        if (HueDistance(candidate.Hue, now.Hue) <= SameHue && Math.Abs(candidate.Saturation - now.Saturation) <= SameSaturation)
            return null;

        var currentVotes = colors.Count(color => HueDistance(color.Hue, now.Hue) <= 15 && Math.Abs(color.Saturation - now.Saturation) <= SameSaturation);
        return members.Count >= Margin * currentVotes ? candidate : null;
    }

    // a circular mean of the hues: 350° and 10° average to 0°, not 180°
    static HsColor Mean(List<HsColor> colors)
    {
        double x = 0, y = 0;
        foreach (var color in colors)
        {
            var radians = color.Hue * Math.PI / 180;
            x += Math.Cos(radians);
            y += Math.Sin(radians);
        }
        var hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        return new HsColor(hue, colors.Average(color => color.Saturation));
    }

    static int WindowVotes(int[] votes, int bin) => votes[Wrap(bin - 1)] + votes[bin] + votes[Wrap(bin + 1)];

    static int Bin(double hue) => (int)(hue / 360 * Bins) % Bins;

    static int Wrap(int bin) => (bin + Bins) % Bins;

    static int CircularDistance(int a, int b)
    {
        var distance = Math.Abs(a - b) % Bins;
        return Math.Min(distance, Bins - distance);
    }

    static double HueDistance(double a, double b)
    {
        var distance = Math.Abs(a - b) % 360;
        return Math.Min(distance, 360 - distance);
    }
}
