using System.Threading.Channels;
using PlexLightSync.Lights;

namespace PlexLightSync.Sync;

/// <summary>
/// The messages for the sync engine: playback events from the poll loop, switch changes from home
/// assistant, and the saved colors from the broker. The engine handles them one at a time, on its own
/// loop, so its state needs no locks.
/// </summary>
public sealed class SyncInbox
{
    readonly Channel<object> _channel = Channel.CreateUnbounded<object>();

    public ChannelReader<object> Reader => _channel.Reader;

    public void Post(object message) => _channel.Writer.TryWrite(message);
}

/// <summary>Someone turned the "Movie sync" switch on or off.</summary>
public sealed record MovieSyncSwitched(bool On);

/// <summary>The saved colors arrived from the broker, or changed. Null means that none are saved.</summary>
public sealed record SavedColorsLoaded(IReadOnlyDictionary<string, BulbState>? Saved);
