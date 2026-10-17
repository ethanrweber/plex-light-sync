namespace PlexLightSync.Video;

/// <summary>One decoded frame, scaled down to 64×36 pixels.</summary>
/// <param name="Position">The playback position of the frame.</param>
/// <param name="Pixels">8-bit rgb, three bytes per pixel, row by row: <see cref="ByteCount"/> bytes.</param>
public sealed record VideoFrame(TimeSpan Position, byte[] Pixels)
{
    public const int Width = 64;
    public const int Height = 36;
    public const int ByteCount = Width * Height * 3;
}
