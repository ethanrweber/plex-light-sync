using System.Text.Json;
using PlexLightSync.Lights;

namespace PlexLightSync.Sync;

/// <summary>
/// The bulb colors from before the sync. They live in a retained mqtt message, so that a restart in
/// the middle of a movie does not lose them.
/// </summary>
public sealed class SavedColors(IMqttBus bus)
{
    public const string Topic = "plex-light-sync/saved_colors";

    public Task SaveAsync(IReadOnlyDictionary<string, BulbState> states, CancellationToken cancellationToken) =>
        bus.PublishAsync(Topic, JsonSerializer.Serialize(states), retain: true, cancellationToken);

    /// <summary>An empty retained message deletes the saved colors from the broker.</summary>
    public Task ClearAsync(CancellationToken cancellationToken) =>
        bus.PublishAsync(Topic, "", retain: true, cancellationToken);

    /// <summary>Calls the handler with the saved colors at start (if any), and after each save or clear.</summary>
    public Task SubscribeAsync(Func<IReadOnlyDictionary<string, BulbState>?, Task> handler, CancellationToken cancellationToken) =>
        bus.SubscribeAsync(Topic, payload => handler(Parse(payload)), cancellationToken);

    internal static IReadOnlyDictionary<string, BulbState>? Parse(string payload) =>
        payload.Length == 0 ? null : JsonSerializer.Deserialize<Dictionary<string, BulbState>>(payload);
}
