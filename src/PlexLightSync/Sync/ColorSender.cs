using PlexLightSync.Color;

namespace PlexLightSync.Sync;

/// <summary>
/// Decides when a color goes out: at most one every <paramref name="minGap"/>, and never one that is
/// almost the same as the last one. A new command restarts the fade, so small changes only make the
/// light less smooth.
/// </summary>
public sealed class ColorSender(TimeSpan minGap)
{
    const double SimilarHue = 8;           // degrees
    const double SimilarSaturation = 0.08;
    const double PaleSaturation = 0.15;    // below this, the hue hardly shows

    HsColor? _last;
    TimeSpan? _lastSentAt;
    HsColor? _waiting;

    /// <summary>The last color that went out, or null after a <see cref="Reset"/>.</summary>
    public HsColor? Last => _last;

    /// <summary>Offers a color. A newer offer replaces one that still waits.</summary>
    public void Offer(HsColor color) => _waiting = color;

    /// <summary>The color to send now, or null. <paramref name="now"/> is any steady clock.</summary>
    public HsColor? TakeReady(TimeSpan now)
    {
        if (_waiting is not { } color)
            return null;

        if (_last is { } last && IsSimilar(last, color))
        {
            _waiting = null;
            return null;
        }

        if (_lastSentAt is { } sentAt && now - sentAt < minGap)
            return null;

        _waiting = null;
        _last = color;
        _lastSentAt = now;
        return color;
    }

    /// <summary>Forgets the last color, so that the next one goes out even if it is the same. Use it after a restore.</summary>
    public void Reset()
    {
        _last = null;
        _waiting = null;
    }

    internal static bool IsSimilar(HsColor a, HsColor b)
    {
        if (Math.Abs(a.Saturation - b.Saturation) >= SimilarSaturation)
            return false;
        if (Math.Min(a.Saturation, b.Saturation) < PaleSaturation)
            return true;
        var hueDistance = Math.Abs(a.Hue - b.Hue) % 360;
        return Math.Min(hueDistance, 360 - hueDistance) < SimilarHue;
    }
}
