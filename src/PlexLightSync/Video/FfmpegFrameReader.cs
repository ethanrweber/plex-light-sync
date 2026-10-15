using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace PlexLightSync.Video;

/// <summary>
/// Runs ffmpeg and reads its frames. The frames come from stdout, and their timestamps come from
/// stderr, one line for each frame.
/// </summary>
/// <remarks>
/// The reader reads stdout only when the caller asks for the next frame. If the caller waits, the pipe
/// fills, and ffmpeg waits too. That is how the caller limits how far ffmpeg reads ahead.
/// </remarks>
public sealed partial class FfmpegFrameReader : IFrameReader
{
    readonly Process _process;
    readonly TimeSpan _start;
    readonly ILogger _logger;
    readonly Channel<TimeSpan?> _timestamps = Channel.CreateUnbounded<TimeSpan?>();
    readonly Task _stderr;

    FfmpegFrameReader(Process process, TimeSpan start, ILogger logger)
    {
        _process = process;
        _start = start;
        _logger = logger;
        _stderr = ReadStderrAsync();
    }

    /// <param name="start">The start position in the arguments. ffmpeg's timestamps count from it.</param>
    public static FfmpegFrameReader Start(IReadOnlyList<string> arguments, TimeSpan start, ILogger logger)
    {
        var info = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg did not start.");
        return new FfmpegFrameReader(process, start, logger);
    }

    /// <summary>The frames in the order that ffmpeg writes them. That order is not always the playback order.</summary>
    public async IAsyncEnumerable<VideoFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stdout = _process.StandardOutput.BaseStream;
        while (true)
        {
            var pixels = new byte[VideoFrame.ByteCount];
            var read = await stdout.ReadAtLeastAsync(pixels, pixels.Length, throwOnEndOfStream: false, cancellationToken);
            if (read < pixels.Length)
                break; // ffmpeg is done

            var position = await _timestamps.Reader.ReadAsync(cancellationToken);
            if (position is { } known)
                yield return new VideoFrame(known, pixels);
            // a frame without a usable timestamp cannot be scheduled, so it is dropped
        }

        await _process.WaitForExitAsync(cancellationToken);
        if (_process.ExitCode != 0)
            _logger.LogWarning("ffmpeg stopped with exit code {ExitCode}", _process.ExitCode);
    }

    async Task ReadStderrAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line)
        {
            var stats = StatsLine().Match(line);
            if (stats.Success)
            {
                var known = double.TryParse(stats.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
                _timestamps.Writer.TryWrite(known ? _start + TimeSpan.FromSeconds(seconds) : null);
            }
            else if (line.Length > 0)
            {
                _logger.LogWarning("ffmpeg: {Line}", line);
            }
        }
        _timestamps.Writer.TryComplete();
    }

    [GeneratedRegex(@"^\d+ (\S+)$")]
    private static partial Regex StatsLine();

    /// <summary>Stops ffmpeg if it still runs.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // it exited in the meantime
        }
        await _process.WaitForExitAsync();
        await _stderr;
        _process.Dispose();
    }
}
