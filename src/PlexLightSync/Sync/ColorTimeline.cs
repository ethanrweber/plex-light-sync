using PlexLightSync.Color;

namespace PlexLightSync.Sync;

/// <summary>
/// The picked colors of the frames, by timestamp: the frames just before the playback position, and
/// the frames that ffmpeg read ahead. The frame pump adds to it, and the engine reads from it, on
/// different threads, so every member locks.
/// </summary>
public sealed class ColorTimeline
{
    readonly Lock _lock = new();
    readonly SortedList<TimeSpan, ColorPick> _picks = [];
    TimeSpan? _horizon;

    /// <summary>
    /// Adds the pick of a frame. ffmpeg can deliver frames out of order, and a late old frame is fine:
    /// the readers look at times, not at the order of arrival.
    /// </summary>
    public void Add(TimeSpan position, ColorPick pick)
    {
        lock (_lock)
        {
            _picks[position] = pick;
            if (_horizon is not { } horizon || position > horizon)
                _horizon = position;
        }
    }

    /// <summary>The newest timestamp that arrived: how far the frames reach ahead. Null if nothing came yet.</summary>
    public TimeSpan? Horizon
    {
        get { lock (_lock) return _horizon; }
    }

    /// <summary>The picks from <paramref name="from"/> to <paramref name="to"/>, both included, in time order.</summary>
    public IReadOnlyList<ColorPick> Between(TimeSpan from, TimeSpan to)
    {
        lock (_lock)
            return [.. _picks.Where(pick => pick.Key >= from && pick.Key <= to).Select(pick => pick.Value)];
    }

    /// <summary>The newest pick at or before <paramref name="position"/>, or null.</summary>
    public ColorPick? Latest(TimeSpan position)
    {
        lock (_lock)
        {
            ColorPick? latest = null;
            foreach (var (time, pick) in _picks)
            {
                if (time > position)
                    break;
                latest = pick;
            }
            return latest;
        }
    }

    /// <summary>
    /// Forgets the picks before <paramref name="position"/>, except the newest of them: with keyframes
    /// only, that one can still be the frame on screen.
    /// </summary>
    public void DropBefore(TimeSpan position)
    {
        lock (_lock)
        {
            while (_picks.Count > 1 && _picks.Keys[1] <= position)
                _picks.RemoveAt(0);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _picks.Clear();
            _horizon = null;
        }
    }
}
