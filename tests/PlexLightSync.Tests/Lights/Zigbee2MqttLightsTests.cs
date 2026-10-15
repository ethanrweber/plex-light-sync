using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlexLightSync.Color;
using PlexLightSync.Lights;

namespace PlexLightSync.Tests.Lights;

public sealed class Zigbee2MqttLightsTests
{
    // shaped like zigbee2mqtt's retained bridge messages, with made-up addresses and names
    const string Devices = """
        [
          { "ieee_address": "0x01", "friendly_name": "Coordinator", "type": "Coordinator" },
          { "ieee_address": "0xa1", "friendly_name": "sconce-left", "type": "Router" },
          { "ieee_address": "0xa2", "friendly_name": "sconce-right", "type": "Router" },
          { "ieee_address": "0xb1", "friendly_name": "kitchen", "type": "Router" }
        ]
        """;

    const string Groups = """
        [
          { "id": 1, "friendly_name": "sconces", "members": [ { "ieee_address": "0xa1", "endpoint": 11 }, { "ieee_address": "0xa2", "endpoint": 11 } ], "scenes": [] },
          { "id": 2, "friendly_name": "kitchen-group", "members": [ { "ieee_address": "0xb1", "endpoint": 11 } ], "scenes": [] }
        ]
        """;

    // what a hue bulb in white mode reports
    const string WhiteOn = """{"brightness":193,"color":{"x":0.3983,"y":0.3871},"color_mode":"color_temp","color_temp":275,"state":"ON"}""";
    const string Off = """{"state":"OFF"}""";

    readonly FakeMqttBus _bus = new();
    readonly Zigbee2MqttLights _lights;

    public Zigbee2MqttLightsTests() =>
        _lights = new Zigbee2MqttLights(_bus, Options.Create(new LightOptions { Group = "sconces" }), NullLogger<Zigbee2MqttLights>.Instance);

    async Task StartWithBridgeMessagesAsync()
    {
        await _lights.StartAsync(TestContext.Current.CancellationToken);
        await _bus.DeliverAsync("zigbee2mqtt/bridge/devices", Devices);
        await _bus.DeliverAsync("zigbee2mqtt/bridge/groups", Groups);
    }

    [Fact]
    public async Task The_group_members_are_found_followed_and_asked_for_their_state()
    {
        await StartWithBridgeMessagesAsync();

        Assert.Equal(["sconce-left", "sconce-right"], _lights.Bulbs);
        Assert.Contains("zigbee2mqtt/sconce-left", _bus.Subscriptions);
        Assert.Contains("zigbee2mqtt/sconce-right", _bus.Subscriptions);
        Assert.DoesNotContain("zigbee2mqtt/kitchen", _bus.Subscriptions);
        Assert.Single(_bus.PayloadsFor("zigbee2mqtt/sconce-left/get"));
        Assert.Single(_bus.PayloadsFor("zigbee2mqtt/sconce-right/get"));
    }

    [Fact]
    public async Task A_second_groups_message_does_not_ask_again()
    {
        await StartWithBridgeMessagesAsync();

        await _bus.DeliverAsync("zigbee2mqtt/bridge/groups", Groups);

        Assert.Single(_bus.PayloadsFor("zigbee2mqtt/sconce-left/get"));
    }

    [Fact]
    public async Task Bulb_messages_keep_the_state_of_each_bulb()
    {
        await StartWithBridgeMessagesAsync();

        await _bus.DeliverAsync("zigbee2mqtt/sconce-left", WhiteOn);
        await _bus.DeliverAsync("zigbee2mqtt/sconce-right", Off);

        Assert.True(_lights.AnyOn);
        Assert.Equal(new BulbState(true, "color_temp", 0.3983, 0.3871, null, null, 275), _lights.States["sconce-left"]);
        Assert.False(_lights.States["sconce-right"].IsOn);
    }

    [Fact]
    public async Task With_every_bulb_off_none_is_on()
    {
        await StartWithBridgeMessagesAsync();

        await _bus.DeliverAsync("zigbee2mqtt/sconce-left", Off);
        await _bus.DeliverAsync("zigbee2mqtt/sconce-right", Off);

        Assert.False(_lights.AnyOn);
    }

    [Fact]
    public async Task A_color_goes_to_the_group_with_a_fade()
    {
        await StartWithBridgeMessagesAsync();

        await _lights.SetColorAsync(new HsColor(210, 0.65), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(["""{"color":{"hue":210,"saturation":65},"transition":2}"""], _bus.PayloadsFor("zigbee2mqtt/sconces/set"));
    }

    [Fact]
    public async Task A_restore_changes_only_the_bulbs_that_are_on()
    {
        await StartWithBridgeMessagesAsync();
        await _bus.DeliverAsync("zigbee2mqtt/sconce-left", WhiteOn);
        await _bus.DeliverAsync("zigbee2mqtt/sconce-right", Off);
        var saved = _lights.States;

        await _lights.RestoreAsync(saved, TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(["""{"color_temp":275,"transition":3}"""], _bus.PayloadsFor("zigbee2mqtt/sconce-left/set"));
        Assert.Empty(_bus.PayloadsFor("zigbee2mqtt/sconce-right/set"));
    }
}
