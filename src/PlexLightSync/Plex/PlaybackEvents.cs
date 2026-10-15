namespace PlexLightSync.Plex;

/// <summary>A change in the playback of the followed player.</summary>
public abstract record PlaybackEvent;

/// <summary>A new item plays (or is paused). Autoplay of the next episode also starts a new item.</summary>
public sealed record PlaybackStarted(PlayingItem Item, TimeSpan Position, bool IsPlaying) : PlaybackEvent;

/// <summary>Playback paused or buffers. The position does not move until it resumes.</summary>
public sealed record PlaybackPaused(TimeSpan Position, PlayerState State) : PlaybackEvent;

public sealed record PlaybackResumed(TimeSpan Position) : PlaybackEvent;

/// <summary>Someone skipped: a report was far from the estimate. <see cref="Jump"/> is the difference.</summary>
public sealed record PlaybackSeeked(TimeSpan Position, TimeSpan Jump) : PlaybackEvent;

/// <summary>A normal report: the estimate was close. <see cref="Drift"/> shows how close.</summary>
public sealed record PositionCorrected(TimeSpan Position, TimeSpan Drift) : PlaybackEvent;

public sealed record PlaybackStopped : PlaybackEvent;
