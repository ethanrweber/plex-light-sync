using PlexLightSync.Video;

namespace PlexLightSync.Tests.Video;

public sealed class FfmpegArgumentsTests
{
    static string ValueAfter(IReadOnlyList<string> arguments, string option) =>
        arguments[arguments.ToList().IndexOf(option) + 1];

    [Fact]
    public void The_token_is_an_http_header_line()
    {
        var arguments = FfmpegArguments.Build("https://plex.example/file.mkv", TimeSpan.Zero, isHdr: false, token: "secret");

        Assert.Equal("X-Plex-Token: secret\r\n", ValueAfter(arguments, "-headers"));
        Assert.DoesNotContain(arguments, argument => argument.Contains("secret") && argument != "X-Plex-Token: secret\r\n");
    }

    [Fact]
    public void A_local_file_gets_no_header()
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr: false);

        Assert.DoesNotContain("-headers", arguments);
    }

    [Fact]
    public void The_seek_comes_before_the_input_so_that_ffmpeg_seeks_in_the_file()
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.FromSeconds(90.5), isHdr: false).ToList();

        Assert.Equal("90.500", ValueAfter(arguments, "-ss"));
        Assert.True(arguments.IndexOf("-ss") < arguments.IndexOf("-i"));
    }

    [Fact]
    public void The_decoder_runs_on_one_thread_to_save_memory()
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr: false).ToList();

        Assert.Equal("1", ValueAfter(arguments, "-threads"));
        Assert.True(arguments.IndexOf("-threads") < arguments.IndexOf("-i")); // an input option: it applies to the decoder
    }

    [Theory]
    [InlineData(false, "scale=64:36")]
    [InlineData(true, "tonemap=hable")]
    public void Hdr_files_get_the_tone_map(bool isHdr, string expected)
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr);

        Assert.Contains(expected, ValueAfter(arguments, "-vf"));
    }

    [Fact]
    public void Without_a_frame_rate_only_the_keyframes_are_decoded()
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr: false);

        Assert.Equal("nokey", ValueAfter(arguments, "-skip_frame"));
        Assert.Equal("scale=64:36", ValueAfter(arguments, "-vf"));
    }

    [Theory]
    [InlineData(false, "fps=2,scale=64:36")]
    [InlineData(true, "fps=2,zscale=w=64:h=36,")]
    public void A_frame_rate_decodes_the_reference_frames_and_picks_the_frames_first(bool isHdr, string expectedStart)
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr, framesPerSecond: 2);

        Assert.Equal("noref", ValueAfter(arguments, "-skip_frame"));
        Assert.StartsWith(expectedStart, ValueAfter(arguments, "-vf"));
    }

    [Fact]
    public void A_fractional_frame_rate_is_written_with_a_dot()
    {
        var arguments = FfmpegArguments.Build("/tmp/clip.mkv", TimeSpan.Zero, isHdr: false, framesPerSecond: 1 / 0.7);

        Assert.Equal("fps=1.429,scale=64:36", ValueAfter(arguments, "-vf"));
    }
}
