using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PlexLightSync.Lights;

namespace PlexLightSync.Tests.Lights;

public sealed class HomeAssistantEntitiesTests
{
    const string Command = "plex-light-sync/movie_sync/set";
    const string State = "plex-light-sync/movie_sync/state";
    const string Status = "plex-light-sync/status";

    readonly FakeMqttBus _bus = new();
    readonly HomeAssistantEntities _entities;

    public HomeAssistantEntitiesTests() =>
        _entities = new HomeAssistantEntities(_bus, NullLogger<HomeAssistantEntities>.Instance);

    [Fact]
    public async Task A_connect_publishes_both_entities_and_their_states_as_retained_messages()
    {
        await _entities.StartAsync(TestContext.Current.CancellationToken);
        await _bus.ConnectAsync();

        Assert.Equal(
            [
                "homeassistant/switch/plex_light_sync/movie_sync/config",
                "homeassistant/sensor/plex_light_sync/status/config",
                State,
                Status,
            ],
            _bus.Published.Select(message => message.Topic));
        Assert.All(_bus.Published, message => Assert.True(message.Retain));
        Assert.Equal(["ON"], _bus.PayloadsFor(State));
        Assert.Equal(["idle"], _bus.PayloadsFor(Status));
    }

    [Fact]
    public void The_switch_config_points_home_assistant_at_the_right_topics()
    {
        var config = JsonDocument.Parse(HomeAssistantEntities.SwitchConfig()).RootElement;

        Assert.Equal(Command, config.GetProperty("command_topic").GetString());
        Assert.Equal(State, config.GetProperty("state_topic").GetString());
        Assert.Equal(MqttBus.AvailabilityTopic, config.GetProperty("availability_topic").GetString());
        Assert.True(config.GetProperty("retain").GetBoolean());
        Assert.Equal("plex_light_sync", config.GetProperty("device").GetProperty("identifiers")[0].GetString());
    }

    [Fact]
    public void The_status_sensor_lists_every_status()
    {
        var config = JsonDocument.Parse(HomeAssistantEntities.StatusConfig()).RootElement;

        Assert.Equal(["idle", "syncing", "paused", "error"], config.GetProperty("options").EnumerateArray().Select(option => option.GetString()));
    }

    [Fact]
    public async Task A_command_switches_movie_sync_and_reports_the_new_state()
    {
        await _entities.StartAsync(TestContext.Current.CancellationToken);
        var changes = new List<bool>();
        _entities.MovieSyncChanged += on => { changes.Add(on); return Task.CompletedTask; };

        await _bus.DeliverAsync(Command, "OFF");

        Assert.False(_entities.MovieSyncOn);
        Assert.Equal([false], changes);
        Assert.Equal(["OFF"], _bus.PayloadsFor(State));
    }

    [Fact]
    public async Task A_repeated_command_reports_the_state_but_raises_no_change()
    {
        await _entities.StartAsync(TestContext.Current.CancellationToken);
        var changes = 0;
        _entities.MovieSyncChanged += _ => { changes++; return Task.CompletedTask; };

        await _bus.DeliverAsync(Command, "ON"); // the switch starts on

        Assert.Equal(0, changes);
        Assert.Equal(["ON"], _bus.PayloadsFor(State));
    }

    [Fact]
    public async Task A_status_is_published_only_when_it_changes()
    {
        var token = TestContext.Current.CancellationToken;

        await _entities.SetStatusAsync(SyncStatus.Syncing, token);
        await _entities.SetStatusAsync(SyncStatus.Syncing, token);
        await _entities.SetStatusAsync(SyncStatus.Paused, token);

        Assert.Equal(["syncing", "paused"], _bus.PayloadsFor(Status));
    }
}
