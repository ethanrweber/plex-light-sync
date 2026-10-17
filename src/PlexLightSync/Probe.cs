using System.Globalization;
using Microsoft.Extensions.Options;
using PlexLightSync.Color;
using PlexLightSync.Plex;
using PlexLightSync.Sync;
using PlexLightSync.Video;

namespace PlexLightSync;

/// <summary>
/// A developer tool: prints the picked color of each frame that the service reads from a Plex item, and
/// the color that the lights would show, with color swatches, without lights. Use it to tune the color
/// and sync settings.
/// </summary>
/// <remarks>
/// <c>dotnet run --project src/PlexLightSync -- probe &lt;ratingKey&gt; [start h:mm:ss] [frames]</c>.
/// The rating key is in the service log ("Started 12345 ...") and in the Plex web address of the item.
/// </remarks>
public sealed class Probe(PlexClient plex, FrameSource frames, ColorPicker picker, IOptions<SyncOptions> sync)
{
    public const string Usage = "usage: probe <ratingKey> [start h:mm:ss] [frames]";

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length is < 1 or > 3)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var start = args.Length > 1 ? TimeSpan.Parse(args[1], CultureInfo.InvariantCulture) : TimeSpan.Zero;
        var limit = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20;

        var file = (await plex.GetMediaFilesAsync(args[0], cancellationToken)).FirstOrDefault();
        if (file is null)
        {
            Console.Error.WriteLine($"Plex item {args[0]} has no file.");
            return 1;
        }

        var framesPerSecond = sync.Value.FramesPerSecondFor(file);
        var mode = framesPerSecond is { } rate ? rate.ToString("0.#", CultureInfo.InvariantCulture) + " frames/s" : "keyframes";
        Console.WriteLine($"{file.PartKey} ({(file.IsHdr ? "hdr" : "sdr")}, {file.Width} px, {mode}), from {start.ToClock()}");
        var picks = new List<(TimeSpan Position, ColorPick Pick)>();
        await using (var reader = frames.Start(file, start, framesPerSecond))
        {
            await foreach (var frame in reader.ReadFramesAsync(cancellationToken))
            {
                picks.Add((frame.Position, picker.Pick(frame)));
                if (picks.Count == limit)
                    break;
            }
        }
        if (picks.Count == 0)
            return 0;
        picks.Sort((a, b) => a.Position.CompareTo(b.Position));

        var sends = Simulate(picks, keyframesOnly: framesPerSecond is null);
        Console.WriteLine($"{"frame",10}  {"",8}  {"picked color",-30}  lights");
        var previous = TimeSpan.MinValue;
        foreach (var (position, pick) in picks)
        {
            var lights = sends.LastOrDefault(send => send.At <= position).Color;
            var changed = sends.Any(send => send.At > previous && send.At <= position);
            Console.WriteLine($"{position.ToClock(),10}  {Swatch(pick.Color)}  {Describe(pick),-30}  {Swatch(lights)}{(changed ? "  ◀ change" : "")}");
            previous = position;
        }
        PrintSummary(picks.Select(pick => pick.Position).ToList(), sends);
        return 0;
    }

    // plays the frames back on a simulated clock, with the engine's tick, vote, and send rules
    List<(TimeSpan At, HsColor? Color)> Simulate(List<(TimeSpan Position, ColorPick Pick)> picks, bool keyframesOnly)
    {
        var settings = sync.Value;
        var timeline = new ColorTimeline();
        foreach (var (position, pick) in picks)
            timeline.Add(position, pick);

        var sender = new ColorSender(settings.MinGap);
        var sends = new List<(TimeSpan At, HsColor? Color)>();
        for (var now = picks[0].Position; now <= picks[^1].Position; now += SyncEngine.Tick)
        {
            var at = now + settings.SendOffset + settings.Fade / 2;
            if (ColorVote.Decide(timeline, at, settings.Window, keyframesOnly, sender.Last) is { } color)
                sender.Offer(color);
            if (sender.TakeReady(now) is { } ready)
                sends.Add((now, ready));
        }
        return sends;
    }

    // the light changes per minute, and a line per 30 s: "y" for a frame with a change before it, "x" without
    static void PrintSummary(List<TimeSpan> positions, List<(TimeSpan At, HsColor? Color)> sends)
    {
        var minutes = (positions[^1] - positions[0]).TotalMinutes;
        Console.WriteLine();
        Console.WriteLine($"{sends.Count} light changes in {minutes:0.0} min ({(minutes > 0 ? sends.Count / minutes : 0):0.0} per minute)");

        var line = new System.Text.StringBuilder();
        var lineStart = positions[0];
        var previous = TimeSpan.MinValue;
        foreach (var position in positions)
        {
            if (position - lineStart >= TimeSpan.FromSeconds(30))
            {
                Console.WriteLine($"{lineStart.ToClock(),10}  {line}");
                (line, lineStart) = (new(), position);
            }
            line.Append(sends.Any(send => send.At > previous && send.At <= position) ? 'y' : 'x');
            previous = position;
        }
        Console.WriteLine($"{lineStart.ToClock(),10}  {line}");
    }

    // a block in 24-bit color; most terminals (vs code too) show it
    static string Swatch(HsColor? color)
    {
        if (color is not { } known)
            return "  ----  ";
        var (r, g, b) = known.ToRgb();
        return $"\e[48;2;{r};{g};{b}m        \e[0m";
    }

    static string Describe(ColorPick pick) => pick.Color is { } color
        ? $"hue {color.Hue,3:0}°, saturation {color.Saturation:0.00}"
        : $"hold ({pick.Reason})";
}
