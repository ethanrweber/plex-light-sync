using PlexLightSync.Plex;

namespace PlexLightSync.Video;

/// <summary>Starts a frame reader on a file. Tests replace it with a fake that needs no ffmpeg.</summary>
public interface IFrameSource
{
    IFrameReader Start(MediaFile file, TimeSpan position);
}

/// <summary>The frames of one file from one start position. Disposing it stops the reading.</summary>
public interface IFrameReader : IAsyncDisposable
{
    IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
