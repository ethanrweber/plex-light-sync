using System.Globalization;

namespace PlexLightSync;

internal static class TimeFormat
{
    /// <summary>Formats a position like a player does: 1:02:03.4. A position before the start gets a minus.</summary>
    public static string ToClock(this TimeSpan time) =>
        (time < TimeSpan.Zero ? "-" : "") + time.Duration().ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture);

    /// <summary>Formats a difference with its sign: +0.4 s.</summary>
    public static string ToSignedSeconds(this TimeSpan time) =>
        time.TotalSeconds.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " s";
}
