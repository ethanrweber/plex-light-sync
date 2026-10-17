using Microsoft.Extensions.Options;
using PlexLightSync.Plex;

namespace PlexLightSync.Video;

/// <summary>Starts ffmpeg on a Plex file.</summary>
public sealed class FrameSource(IOptions<PlexOptions> plex, ILogger<FfmpegFrameReader> logger) : IFrameSource
{
    public IFrameReader Start(MediaFile file, TimeSpan position, double? framesPerSecond)
    {
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        var url = new Uri(new Uri(plex.Value.Url), file.PartKey).ToString();
        var arguments = FfmpegArguments.Build(url, position, file.IsHdr, plex.Value.Token, framesPerSecond);
        return FfmpegFrameReader.Start(arguments, position, logger);
    }
}
