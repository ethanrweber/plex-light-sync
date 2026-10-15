using System.Reflection;
using System.Text.Json;

namespace PlexLightSync.Lights;

/// <summary>What the service does now. Home assistant shows it in the status sensor.</summary>
public enum SyncStatus { Idle, Syncing, Paused, Error }

/// <summary>
/// The service's entities in home assistant, through mqtt discovery: the "Movie sync" switch and the
/// status sensor.
/// </summary>
/// <remarks>
/// Home assistant publishes the switch commands with the retain flag. So after a restart, the broker
/// gives the service the last command at once, and the switch keeps its state. Before the first
/// command, the switch is on.
/// </remarks>
public sealed class HomeAssistantEntities : IHostedService
{
    const string Base = "plex-light-sync";
    const string SwitchCommandTopic = Base + "/movie_sync/set";
    const string SwitchStateTopic = Base + "/movie_sync/state";
    const string StatusTopic = Base + "/status";
    const string SwitchConfigTopic = "homeassistant/switch/plex_light_sync/movie_sync/config";
    const string StatusConfigTopic = "homeassistant/sensor/plex_light_sync/status/config";

    static readonly string Version =
        typeof(HomeAssistantEntities).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    readonly IMqttBus _bus;
    readonly ILogger<HomeAssistantEntities> _logger;
    volatile bool _movieSyncOn = true;
    SyncStatus _status = SyncStatus.Idle;

    public HomeAssistantEntities(IMqttBus bus, ILogger<HomeAssistantEntities> logger)
    {
        _bus = bus;
        _logger = logger;
        _bus.OnConnected(PublishAllAsync);
    }

    /// <summary>The state of the "Movie sync" switch.</summary>
    public bool MovieSyncOn => _movieSyncOn;

    /// <summary>Raised when someone turns the switch on or off.</summary>
    public event Func<bool, Task>? MovieSyncChanged;

    public Task StartAsync(CancellationToken cancellationToken) =>
        _bus.SubscribeAsync(SwitchCommandTopic, OnSwitchCommandAsync, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SetStatusAsync(SyncStatus status, CancellationToken cancellationToken)
    {
        if (status == _status)
            return;
        _status = status;
        await _bus.PublishAsync(StatusTopic, Lower(status), retain: true, cancellationToken);
    }

    async Task OnSwitchCommandAsync(string payload)
    {
        bool on;
        if (payload == "ON")
            on = true;
        else if (payload == "OFF")
            on = false;
        else
        {
            _logger.LogWarning("Unknown Movie sync command {Payload}", payload);
            return;
        }

        await _bus.PublishAsync(SwitchStateTopic, payload, retain: true);
        if (on == _movieSyncOn)
            return;

        _movieSyncOn = on;
        _logger.LogInformation("Movie sync turned {State}", on ? "on" : "off");
        if (MovieSyncChanged is { } handler)
            await handler(on);
    }

    async Task PublishAllAsync(CancellationToken cancellationToken)
    {
        await _bus.PublishAsync(SwitchConfigTopic, SwitchConfig(), retain: true, cancellationToken);
        await _bus.PublishAsync(StatusConfigTopic, StatusConfig(), retain: true, cancellationToken);
        await _bus.PublishAsync(SwitchStateTopic, _movieSyncOn ? "ON" : "OFF", retain: true, cancellationToken);
        await _bus.PublishAsync(StatusTopic, Lower(_status), retain: true, cancellationToken);
    }

    static string Lower(SyncStatus status) => status.ToString().ToLowerInvariant();

    // the discovery payloads use home assistant's own key names
    static object Device => new
    {
        identifiers = new[] { "plex_light_sync" },
        name = "Plex light sync",
        manufacturer = "plex-light-sync",
        model = "plex-light-sync",
        sw_version = Version,
    };

    static object Origin => new { name = "plex-light-sync", sw = Version, url = "https://github.com/ethanrweber/plex-light-sync" };

    internal static string SwitchConfig() => JsonSerializer.Serialize(new
    {
        name = "Movie sync",
        unique_id = "plex_light_sync_movie_sync",
        command_topic = SwitchCommandTopic,
        state_topic = SwitchStateTopic,
        retain = true, // home assistant retains the commands; see the remarks above
        availability_topic = MqttBus.AvailabilityTopic,
        icon = "mdi:television-ambient-light",
        device = Device,
        origin = Origin,
    });

    internal static string StatusConfig() => JsonSerializer.Serialize(new
    {
        name = "Status",
        unique_id = "plex_light_sync_status",
        state_topic = StatusTopic,
        device_class = "enum",
        options = Enum.GetValues<SyncStatus>().Select(Lower).ToArray(),
        availability_topic = MqttBus.AvailabilityTopic,
        icon = "mdi:movie-open-play",
        device = Device,
        origin = Origin,
    });
}
