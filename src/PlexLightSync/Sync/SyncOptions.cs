using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace PlexLightSync.Sync;

/// <summary>The timing of the sync. All values are in seconds.</summary>
public sealed class SyncOptions
{
    /// <summary>How long the sconces fade to a new color.</summary>
    [ConfigurationKeyName("sync_fade"), Display(Name = "sync_fade"), Range(0.0, 10.0)]
    public double FadeSeconds { get; set; } = 2;

    /// <summary>How early a color goes out before its frame plays, to cover the zigbee delay.</summary>
    [ConfigurationKeyName("sync_send_offset"), Display(Name = "sync_send_offset"), Range(0.0, 5.0)]
    public double SendOffsetSeconds { get; set; } = 0.3;

    /// <summary>The shortest time between two colors. A zigbee network carries about one group command per second.</summary>
    [ConfigurationKeyName("sync_min_gap"), Display(Name = "sync_min_gap"), Range(1.0, 30.0)]
    public double MinGapSeconds { get; set; } = 2;

    /// <summary>How long after the last playback stops the saved colors come back.</summary>
    [ConfigurationKeyName("sync_restore_delay"), Display(Name = "sync_restore_delay"), Range(0.0, 600.0)]
    public double RestoreDelaySeconds { get; set; } = 30;

    /// <summary>How long the sconces fade back to the saved colors.</summary>
    [ConfigurationKeyName("sync_restore_fade"), Display(Name = "sync_restore_fade"), Range(0.0, 10.0)]
    public double RestoreFadeSeconds { get; set; } = 3;

    public TimeSpan Fade => TimeSpan.FromSeconds(FadeSeconds);
    public TimeSpan SendOffset => TimeSpan.FromSeconds(SendOffsetSeconds);
    public TimeSpan MinGap => TimeSpan.FromSeconds(MinGapSeconds);
    public TimeSpan RestoreDelay => TimeSpan.FromSeconds(RestoreDelaySeconds);
    public TimeSpan RestoreFade => TimeSpan.FromSeconds(RestoreFadeSeconds);
}
