using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlexLightSync.Color;
using PlexLightSync.Video;

namespace PlexLightSync.Tests.Video;

/// <summary>Runs the real ffmpeg command on the generated clips.</summary>
public sealed class FfmpegFrameReaderTests(GeneratedClips clips) : IClassFixture<GeneratedClips>
{
    // no saturation boost, so that the tests see the colors as ffmpeg delivers them
    static readonly ColorPicker Picker = new(Options.Create(new ColorOptions { SaturationBoost = 1 }));

    async Task<List<VideoFrame>> ReadAllAsync(string file, TimeSpan start, bool isHdr)
    {
        Assert.SkipUnless(clips.Available, "ffmpeg is not installed");

        await using var reader = FfmpegFrameReader.Start(FfmpegArguments.Build(file, start, isHdr), start, NullLogger.Instance);
        var frames = new List<VideoFrame>();
        await foreach (var frame in reader.ReadFramesAsync(TestContext.Current.CancellationToken))
            frames.Add(frame);
        return frames;
    }

    static double HueOf(VideoFrame frame) => Picker.Pick(frame).Color!.Value.Hue;

    static void AssertHue(double expected, double actual, double tolerance = 10)
    {
        var distance = Math.Abs(expected - actual) % 360;
        Assert.True(Math.Min(distance, 360 - distance) <= tolerance, $"hue {actual:0.0}°, expected {expected:0.0}°");
    }

    [Fact]
    public async Task Each_keyframe_comes_out_once_with_its_position()
    {
        var frames = await ReadAllAsync(clips.Sdr, TimeSpan.Zero, isHdr: false);

        Assert.Equal([0.0, 2.0, 4.0], frames.Select(frame => Math.Round(frame.Position.TotalSeconds, 2)));
        AssertHue(0, HueOf(frames[0]));   // red
        AssertHue(240, HueOf(frames[1])); // blue
        AssertHue(120, HueOf(frames[2])); // green
    }

    [Fact]
    public async Task A_start_between_keyframes_begins_at_the_keyframe_before_it()
    {
        var frames = await ReadAllAsync(clips.Sdr, TimeSpan.FromSeconds(3), isHdr: false);

        Assert.Equal([2.0, 4.0], frames.Select(frame => Math.Round(frame.Position.TotalSeconds, 2)));
        AssertHue(240, HueOf(frames[0]));
    }

    [Fact]
    public async Task The_tone_map_brings_back_the_color_of_an_hdr_file()
    {
        var toneMapped = Picker.Pick((await ReadAllAsync(clips.Hdr, TimeSpan.Zero, isHdr: true))[0]).Color!.Value;
        var plain = Picker.Pick((await ReadAllAsync(clips.Hdr, TimeSpan.Zero, isHdr: false))[0]).Color!.Value;

        AssertHue(6, toneMapped.Hue);
        Assert.True(toneMapped.Saturation > plain.Saturation + 0.15,
            $"tone mapped saturation {toneMapped.Saturation:0.00}, plain {plain.Saturation:0.00}");
    }

    [Fact]
    public async Task Stopping_early_ends_ffmpeg()
    {
        Assert.SkipUnless(clips.Available, "ffmpeg is not installed");
        var reader = FfmpegFrameReader.Start(FfmpegArguments.Build(clips.Sdr, TimeSpan.Zero, isHdr: false), TimeSpan.Zero, NullLogger.Instance);

        await foreach (var _ in reader.ReadFramesAsync(TestContext.Current.CancellationToken))
            break;

        var dispose = reader.DisposeAsync().AsTask();
        Assert.Same(dispose, await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
    }
}
