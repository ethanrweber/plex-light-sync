using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using PlexLightSync.Plex;

namespace PlexLightSync.Sync;

/// <summary>The timing of the sync. All values are in seconds.</summary>
public sealed class SyncOptions
{
    /// <summary>How long the sconces fade to a new color.</summary>
    [ConfigurationKeyName("sync_fade"), Display(Name = "sync_fade"), Range(0.0, 10.0)]
    public double FadeSeconds { get; set; } = 2;

    /// <summary>
    /// How far the color vote looks before and after the moment. A new color must last about this long
    /// before the lights follow it, so flashes and quick cuts do not reach the lights.
    /// </summary>
    [ConfigurationKeyName("sync_window"), Display(Name = "sync_window"), Range(0.0, 10.0)]
    public double WindowSeconds { get; set; } = 3;

    /// <summary>How early a color goes out before its frame plays, to cover the zigbee delay.</summary>
    [ConfigurationKeyName("sync_send_offset"), Display(Name = "sync_send_offset"), Range(0.0, 5.0)]
    public double SendOffsetSeconds { get; set; } = 0.3;

    /// <summary>
    /// The shortest time between two colors. Each color is one group command: a zigbee broadcast. How many
    /// broadcasts a network carries depends on its devices; 0.5 s is the fastest rate that docs/plan.md
    /// tested. A video up to <see cref="MaxSampledWidth"/> also gives one frame per gap.
    /// </summary>
    [ConfigurationKeyName("sync_min_gap"), Display(Name = "sync_min_gap"), Range(0.5, 30.0)]
    public double MinGapSeconds { get; set; } = 0.5;

    /// <summary>How long after the last playback stops the saved colors come back.</summary>
    [ConfigurationKeyName("sync_restore_delay"), Display(Name = "sync_restore_delay"), Range(0.0, 600.0)]
    public double RestoreDelaySeconds { get; set; } = 30;

    /// <summary>How long the sconces fade back to the saved colors.</summary>
    [ConfigurationKeyName("sync_restore_fade"), Display(Name = "sync_restore_fade"), Range(0.0, 10.0)]
    public double RestoreFadeSeconds { get; set; } = 3;

    /// <summary>
    /// The widest video that the service decodes in full (1080p). One core decodes 1080p at 2.2–4.5× real
    /// time, but 4k at 0.6×, so a wider video gives only its keyframes.
    /// </summary>
    public const int MaxSampledWidth = 1920;

    /// <summary>The frames per second to read from a file, or null for only its keyframes (also if the width is unknown).</summary>
    public double? FramesPerSecondFor(MediaFile file) =>
        file.Width is > 0 and <= MaxSampledWidth ? 1 / MinGapSeconds : null;

    public TimeSpan Fade => TimeSpan.FromSeconds(FadeSeconds);
    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
    public TimeSpan SendOffset => TimeSpan.FromSeconds(SendOffsetSeconds);
    public TimeSpan MinGap => TimeSpan.FromSeconds(MinGapSeconds);
    public TimeSpan RestoreDelay => TimeSpan.FromSeconds(RestoreDelaySeconds);
    public TimeSpan RestoreFade => TimeSpan.FromSeconds(RestoreFadeSeconds);
}
