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
}
