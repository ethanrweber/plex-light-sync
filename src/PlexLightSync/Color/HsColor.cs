namespace PlexLightSync.Color;

/// <summary>A light color without brightness: hue in degrees (0 to 360), saturation from 0 to 1.</summary>
public readonly record struct HsColor(double Hue, double Saturation)
{
    /// <summary>The color at full brightness, as 8-bit rgb.</summary>
    public (byte R, byte G, byte B) ToRgb()
    {
        var sector = Hue / 60 % 6;
        var fraction = sector - Math.Floor(sector);
        double max = 1, min = 1 - Saturation, falling = 1 - Saturation * fraction, rising = 1 - Saturation * (1 - fraction);
        var (r, g, b) = (int)sector switch
        {
            0 => (max, rising, min),
            1 => (falling, max, min),
            2 => (min, max, rising),
            3 => (min, falling, max),
            4 => (rising, min, max),
            _ => (max, min, falling),
        };
        return (ToByte(r), ToByte(g), ToByte(b));
    }

    static byte ToByte(double channel) => (byte)Math.Round(channel * 255);
}

/// <summary>Why the picker found no color: the lights hold their last color.</summary>
public enum HoldReason
{
    /// <summary>The frame has a color.</summary>
    None,

    /// <summary>The whole frame is (almost) black: a fade or a cut to black.</summary>
    Black,

    /// <summary>The frame has light but no clear color: gray, white, or black and white film.</summary>
    NoColor,
}

/// <summary>The result of one pick: a color, or the reason to hold the last color.</summary>
public readonly record struct ColorPick(HsColor? Color, HoldReason Reason)
{
    public static ColorPick Black => new(null, HoldReason.Black);
    public static ColorPick NoColor => new(null, HoldReason.NoColor);
    public static ColorPick Of(HsColor color) => new(color, HoldReason.None);
}
