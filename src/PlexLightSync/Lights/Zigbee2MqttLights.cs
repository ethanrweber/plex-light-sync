using Microsoft.Extensions.Options;
using PlexLightSync.Color;

namespace PlexLightSync.Lights;

/// <summary>The lights that the sync engine drives. Tests replace them with a fake.</summary>
public interface ILights
{
    bool AnyOn { get; }

    IReadOnlyDictionary<string, BulbState> States { get; }

    Task SetColorAsync(HsColor color, TimeSpan fade, CancellationToken cancellationToken);

    Task RestoreAsync(IReadOnlyDictionary<string, BulbState> saved, TimeSpan fade, CancellationToken cancellationToken);
}

/// <summary>
/// The bulbs of the configured zigbee2mqtt group: who they are, what state they are in, and the
/// commands that change them.
/// </summary>
/// <remarks>
/// Zigbee2mqtt publishes the group list and the device list as retained messages, so the service
/// finds the bulbs at once. Bulb states are not retained, so the service asks each new bulb for its
/// state, and then follows the bulb's messages.
/// </remarks>
public sealed class Zigbee2MqttLights(IMqttBus bus, IOptions<LightOptions> options, ILogger<Zigbee2MqttLights> logger)
    : IHostedService, ILights
{
    readonly Lock _lock = new();
    readonly Dictionary<string, BulbState> _states = [];
    Dictionary<string, string> _deviceNames = [];
    IReadOnlyList<string>? _memberAddresses;
    IReadOnlyList<string> _bulbs = [];

    string BaseTopic => options.Value.BaseTopic;
    string Group => options.Value.Group;

    /// <summary>The friendly names of the bulbs in the group.</summary>
    public IReadOnlyList<string> Bulbs
    {
        get { lock (_lock) return _bulbs; }
    }

    /// <summary>A copy of the known bulb states, by friendly name.</summary>
    public IReadOnlyDictionary<string, BulbState> States
    {
        get { lock (_lock) return new Dictionary<string, BulbState>(_states); }
    }

    /// <summary>True if at least one bulb of the group is on.</summary>
    public bool AnyOn
    {
        get { lock (_lock) return _bulbs.Any(bulb => _states.TryGetValue(bulb, out var state) && state.IsOn); }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await bus.SubscribeAsync($"{BaseTopic}/bridge/devices", OnDevicesAsync, cancellationToken);
        await bus.SubscribeAsync($"{BaseTopic}/bridge/groups", OnGroupsAsync, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Sends one color to the whole group: one zigbee broadcast.</summary>
    public Task SetColorAsync(HsColor color, TimeSpan fade, CancellationToken cancellationToken) =>
        bus.PublishAsync($"{BaseTopic}/{Group}/set", Zigbee2MqttPayloads.Color(color, fade), cancellationToken: cancellationToken);

    /// <summary>Puts the saved colors back on the bulbs that are on now. It never turns a bulb on or off.</summary>
    public async Task RestoreAsync(IReadOnlyDictionary<string, BulbState> saved, TimeSpan fade, CancellationToken cancellationToken)
    {
        var current = States;
        foreach (var (bulb, savedState) in saved)
        {
            if (!current.TryGetValue(bulb, out var now) || !now.IsOn)
                continue;
            if (Zigbee2MqttPayloads.Restore(savedState, fade) is { } payload)
                await bus.PublishAsync($"{BaseTopic}/{bulb}/set", payload, cancellationToken: cancellationToken);
        }
    }

    Task OnDevicesAsync(string payload)
    {
        var names = Zigbee2MqttPayloads.ParseDeviceNames(payload);
        lock (_lock)
            _deviceNames = names;
        return UpdateBulbsAsync();
    }

    Task OnGroupsAsync(string payload)
    {
        var members = Zigbee2MqttPayloads.ParseGroupMembers(payload, Group);
        if (members is null)
            logger.LogWarning("Zigbee2MQTT has no group {Group}", Group);
        lock (_lock)
            _memberAddresses = members;
        return UpdateBulbsAsync();
    }

    // runs after either list changes; follows the bulbs that are new to the group
    async Task UpdateBulbsAsync()
    {
        IReadOnlyList<string> added;
        lock (_lock)
        {
            if (_memberAddresses is null || _deviceNames.Count == 0)
                return;
            var bulbs = _memberAddresses.Select(address => _deviceNames.GetValueOrDefault(address)).OfType<string>().ToList();
            added = [.. bulbs.Except(_bulbs)];
            if (added.Count == 0 && bulbs.Count == _bulbs.Count)
                return;
            _bulbs = bulbs;
        }

        logger.LogInformation("Zigbee2MQTT group {Group}: {Bulbs}", Group, string.Join(", ", Bulbs));
        foreach (var bulb in added)
        {
            await bus.SubscribeAsync($"{BaseTopic}/{bulb}", payload => OnBulbStateAsync(bulb, payload));
            await bus.PublishAsync($"{BaseTopic}/{bulb}/get", Zigbee2MqttPayloads.StateRequest);
        }
    }

    Task OnBulbStateAsync(string bulb, string payload)
    {
        lock (_lock)
            _states[bulb] = Zigbee2MqttPayloads.ParseBulbState(payload, _states.GetValueOrDefault(bulb));
        logger.LogDebug("Bulb {Bulb}: {State}", bulb, States[bulb]);
        return Task.CompletedTask;
    }
}
