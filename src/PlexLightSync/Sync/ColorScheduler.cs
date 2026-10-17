using PlexLightSync.Color;

namespace PlexLightSync.Sync;

/// <summary>
/// The picked colors that wait for their moment in the video, in timestamp order. The frame pump adds
/// to it, and the engine takes from it, on different threads, so every member locks.
/// </summary>
public sealed class ColorScheduler
{
    readonly Lock _lock = new();
    readonly SortedList<TimeSpan, ColorPick> _queue = [];
    TimeSpan? _lastTaken;

    /// <summary>
    /// Queues the pick of a frame. A frame at or before the last taken one is dropped: ffmpeg can
    /// deliver frames out of order, and an old one must not replace a newer color.
    /// </summary>
    public void Add(TimeSpan position, ColorPick pick)
    {
        lock (_lock)
        {
            if (_lastTaken is { } last && position <= last)
                return;
            _queue[position] = pick;
        }
    }

    /// <summary>The newest queued timestamp: how far the frames reach ahead. Null if nothing came yet.</summary>
    public TimeSpan? Horizon
    {
        get { lock (_lock) return _queue.Count > 0 ? _queue.Keys[^1] : _lastTaken; }
    }

    /// <summary>
    /// Takes the newest pick at or before the position, and drops the older ones. After a start, that is
    /// the frame on screen now. Null if no pick is due.
    /// </summary>
    public ColorPick? TakeDue(TimeSpan position)
    {
        lock (_lock)
        {
            ColorPick? due = null;
            while (_queue.Count > 0 && _queue.Keys[0] <= position)
            {
                due = _queue.Values[0];
                _lastTaken = _queue.Keys[0];
                _queue.RemoveAt(0);
            }
            return due;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _lastTaken = null;
        }
    }
}
