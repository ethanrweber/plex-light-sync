using System.Globalization;
using PlexLightSync.Color;
using PlexLightSync.Plex;
using PlexLightSync.Video;

namespace PlexLightSync;

/// <summary>
/// A developer tool: prints the picked color of each keyframe of a Plex item, with a color swatch,
/// without lights. Use it to tune the color settings.
/// </summary>
/// <remarks>
/// <c>dotnet run --project src/PlexLightSync -- probe &lt;ratingKey&gt; [start h:mm:ss] [frames]</c>.
/// The rating key is in the service log ("Started 12345 ...") and in the Plex web address of the item.
/// </remarks>
public sealed class Probe(PlexClient plex, FrameSource frames, ColorPicker picker)
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

        Console.WriteLine($"{file.PartKey} ({(file.IsHdr ? "hdr" : "sdr")}), from {start.ToClock()}");
        await using var reader = frames.Start(file, start);
        var count = 0;
        await foreach (var frame in reader.ReadFramesAsync(cancellationToken))
        {
            var pick = picker.Pick(frame);
            Console.WriteLine($"{frame.Position.ToClock(),10}  {Swatch(pick.Color)}  {Describe(pick)}");
            if (++count == limit)
                break;
        }
        return 0;
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
