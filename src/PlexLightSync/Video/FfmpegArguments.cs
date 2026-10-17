using System.Globalization;

namespace PlexLightSync.Video;

/// <summary>Builds the ffmpeg command line. docs/plan.md explains the command.</summary>
public static class FfmpegArguments
{
    const string Scale = "scale=64:36";

    // hdr (pq or hlg) to sdr. the first step scales, so that the tone map runs on 64×36 pixels only.
    const string ToneMap =
        "zscale=w=64:h=36,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709," +
        "tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=rgb24";

    /// <param name="input">A URL or a file path.</param>
    /// <param name="start">Where to start. ffmpeg starts at the keyframe before this position.</param>
    /// <param name="token">The Plex token, sent as an http header. Null for a local file.</param>
    /// <param name="framesPerSecond">
    /// Null: decode only the keyframes. Otherwise: decode the frames that other frames refer to, and
    /// write this many frames per second.
    /// </param>
    public static IReadOnlyList<string> Build(string input, TimeSpan start, bool isHdr, string? token = null, double? framesPerSecond = null)
    {
        List<string> arguments = ["-nostdin", "-hide_banner", "-loglevel", "error"];
        if (token is not null)
            arguments.AddRange(["-headers", $"X-Plex-Token: {token}\r\n"]);

        // "noref" skips only the frames that no other frame refers to, so the picture stays intact.
        // it halves the decode work: 1080p decodes at 2.2–4.5× real time on one core.
        var skip = framesPerSecond is null ? "nokey" : "noref";
        var filter = isHdr ? ToneMap : Scale;
        if (framesPerSecond is { } rate)
            filter = $"fps={rate.ToString("0.###", CultureInfo.InvariantCulture)},{filter}";   // first, so that later steps see only the kept frames

        arguments.AddRange([
            "-threads", "1",                       // one decoder thread: half the memory (230 MB for 4k), the same speed
            "-skip_frame", skip,
            "-noaccurate_seek",                    // begin at the keyframe before the start
            "-ss", start.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
            "-i", input,
            "-map", "0:v:0",
            "-vf", filter,
            "-fps_mode", "passthrough",            // one output frame for each frame out of the filter
            "-f", "rawvideo", "-pix_fmt", "rgb24",
            "-stats_mux_pre_fmt", "{n} {t}",       // a "<index> <seconds>" line for each frame...
            "-stats_mux_pre", "pipe:2",            // ...on stderr. the seconds count from the start.
            "pipe:1",
        ]);
        return arguments;
    }
}
