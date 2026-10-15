using System.ComponentModel;
using System.Diagnostics;

namespace PlexLightSync.Tests.Video;

/// <summary>
/// Short test videos, made with ffmpeg when the tests start:
/// <list type="bullet">
/// <item><see cref="Sdr"/>: 2 s red, 2 s blue, 2 s green, with keyframes at exactly 0, 2, and 4 s.</item>
/// <item><see cref="Hdr"/>: 2 s of one red (rgb 192, 48, 32, hue 6°), encoded as hdr10 (pq, bt.2020).</item>
/// </list>
/// </summary>
public sealed class GeneratedClips : IAsyncLifetime
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "plex-light-sync-tests-" + Guid.NewGuid().ToString("N"));

    public string Sdr => Path.Combine(Folder, "sdr.mkv");

    public string Hdr => Path.Combine(Folder, "hdr.mkv");

    /// <summary>False if ffmpeg is not installed. The video tests then skip.</summary>
    public bool Available { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Folder);
        Available =
            await RunFfmpegAsync(
                "-f", "lavfi", "-i", "color=c=red:s=128x72:r=24:d=2",
                "-f", "lavfi", "-i", "color=c=blue:s=128x72:r=24:d=2",
                "-f", "lavfi", "-i", "color=c=lime:s=128x72:r=24:d=2",
                "-filter_complex", "[0][1][2]concat=n=3:v=1:a=0,format=yuv420p",
                "-c:v", "libx264", "-g", "1000", "-force_key_frames", "0,2,4", "-x264-params", "scenecut=0",
                Sdr)
            && await RunFfmpegAsync(
                "-f", "lavfi", "-i", "color=c=0xC03020:s=128x72:r=24:d=2",
                "-vf", "zscale=transferin=bt709:primariesin=bt709:matrixin=bt709:rangein=tv:"
                     + "t=smpte2084:p=bt2020:m=bt2020nc:r=tv:npl=100,format=yuv420p10le",
                "-c:v", "libx265", "-x265-params", "log-level=error",
                "-color_trc", "smpte2084", "-color_primaries", "bt2020", "-colorspace", "bt2020nc",
                Hdr);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Folder, recursive: true);
        return ValueTask.CompletedTask;
    }

    static async Task<bool> RunFfmpegAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in (string[])["-nostdin", "-v", "error", .. arguments])
            info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info)!;
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false; // ffmpeg is not installed
        }
    }
}
