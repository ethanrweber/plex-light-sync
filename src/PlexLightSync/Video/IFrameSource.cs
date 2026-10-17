using PlexLightSync.Plex;

namespace PlexLightSync.Video;

/// <summary>Starts a frame reader on a file. Tests replace it with a fake that needs no ffmpeg.</summary>
public interface IFrameSource
{
    /// <param name="framesPerSecond">Null: only the keyframes. Otherwise: this many frames per second.</param>
    IFrameReader Start(MediaFile file, TimeSpan position, double? framesPerSecond);
}

/// <summary>The frames of one file from one start position. Disposing it stops the reading.</summary>
public interface IFrameReader : IAsyncDisposable
{
    IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
