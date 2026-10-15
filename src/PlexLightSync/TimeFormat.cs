using System.Globalization;

namespace PlexLightSync;

internal static class TimeFormat
{
    /// <summary>Formats a position like a player does: 1:02:03.4. A position before the start gets a minus.</summary>
    public static string ToClock(this TimeSpan time) =>
        (time < TimeSpan.Zero ? "-" : "") + time.Duration().ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture);

    /// <summary>Formats a difference with its sign: +0.4 s. A value that rounds to zero is +0.0 s.</summary>
    public static string ToSignedSeconds(this TimeSpan time)
    {
        // round first: a small negative value (-0.04) would otherwise print as "-+0.0"
        var seconds = Math.Round(time.TotalSeconds, 1);
        return (seconds == 0 ? 0 : seconds).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " s";
    }
}
