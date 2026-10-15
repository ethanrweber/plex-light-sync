namespace PlexLightSync.Plex;

/// <summary>
/// Turns the player's reports into playback events, and estimates the playback position between
/// reports.
/// </summary>
/// <remarks>
/// The poll loop feeds the tracker, and the sync engine reads the position from another thread, so
/// every member locks.
/// <para>
/// The tv reports its position only every 10 s, and the service polls every 1 s. So the tracker keeps
/// its own stopwatch: an anchor (a reported position and the time it arrived) plus the time since
/// then. A new report that is close to the estimate corrects the stopwatch. A report that is far from
/// the estimate means that someone skipped.
/// </para>
/// </remarks>
/// <param name="seekThreshold">A report further than this from the estimate is a skip.</param>
/// <param name="reportDelay">
/// How old a report is, on average, when a poll finds it: half the poll interval.
/// </param>
public sealed class PlaybackTracker(TimeProvider time, TimeSpan seekThreshold, TimeSpan reportDelay)
{
    readonly Lock _lock = new();
    PlayerReport? _last;
    TimeSpan _anchorOffset;
    long _anchorTimestamp;

    public PlayingItem? Item
    {
        get { lock (_lock) return _last?.Item; }
    }

    public bool IsPlaying
    {
        get { lock (_lock) return PlayingNow; }
    }

    /// <summary>The estimated position now, or null if nothing plays.</summary>
    public TimeSpan? Position
    {
        get { lock (_lock) return PositionNow; }
    }

    bool PlayingNow => _last?.State == PlayerState.Playing;

    TimeSpan? PositionNow =>
        _last is null ? null
        : PlayingNow ? _anchorOffset + time.GetElapsedTime(_anchorTimestamp)
        : _anchorOffset;

    /// <summary>Takes the result of one poll. Null means that the player has no session.</summary>
    public PlaybackEvent? Observe(PlayerReport? report)
    {
        lock (_lock)
            return ObserveLocked(report);
    }

    PlaybackEvent? ObserveLocked(PlayerReport? report)
    {
        if (report is null)
        {
            if (_last is null)
                return null;
            _last = null;
            return new PlaybackStopped();
        }

        if (_last is null || report.Item != _last.Item)
        {
            Anchor(report);
            return new PlaybackStarted(report.Item, PositionNow!.Value, PlayingNow);
        }

        // the same report again: the tv has not reported since the last poll
        if (report.Offset == _last.Offset && report.State == _last.State)
            return null;

        var wasPlaying = PlayingNow;
        var estimate = PositionNow!.Value;
        Anchor(report);
        var position = PositionNow!.Value;

        if (wasPlaying && !PlayingNow)
            return new PlaybackPaused(position, report.State);
        if (!wasPlaying && PlayingNow)
            return new PlaybackResumed(position);
        if (!PlayingNow)
            return null; // a skip during a pause only moves the anchor; the resume uses it

        var drift = position - estimate;
        return drift.Duration() > seekThreshold
            ? new PlaybackSeeked(position, drift)
            : new PositionCorrected(position, drift);
    }

    void Anchor(PlayerReport report)
    {
        _last = report;
        _anchorOffset = report.State == PlayerState.Playing ? report.Offset + reportDelay : report.Offset;
        _anchorTimestamp = time.GetTimestamp();
    }
}
