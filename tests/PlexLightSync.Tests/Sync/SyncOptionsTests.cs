using PlexLightSync.Plex;
using PlexLightSync.Sync;

namespace PlexLightSync.Tests.Sync;

public sealed class SyncOptionsTests
{
    static MediaFile File(int width) => new(1, "/library/parts/1/1/file.mkv", IsHdr: false, width);

    [Theory]
    [InlineData(1920, 2.0)]
    [InlineData(1280, 2.0)]
    [InlineData(1921, null)]
    [InlineData(3840, null)]
    [InlineData(0, null)]   // unknown
    public void Files_up_to_1080p_give_one_frame_per_gap_and_wider_files_only_keyframes(int width, double? expected)
    {
        Assert.Equal(expected, new SyncOptions().FramesPerSecondFor(File(width)));
    }

    [Fact]
    public void The_frame_rate_follows_the_minimum_gap()
    {
        Assert.Equal(1.0, new SyncOptions { MinGapSeconds = 1 }.FramesPerSecondFor(File(1920)));
    }
}
