using PlexLightSync.Lights;

namespace PlexLightSync.Tests.Lights;

public sealed class Zigbee2MqttPayloadsTests
{
    static readonly TimeSpan Fade = TimeSpan.FromSeconds(3);

    [Fact]
    public void A_missing_group_is_null()
    {
        Assert.Null(Zigbee2MqttPayloads.ParseGroupMembers("""[{"friendly_name":"other","members":[]}]""", "sconces"));
    }

    [Fact]
    public void A_partial_state_message_keeps_the_values_that_it_leaves_out()
    {
        var before = new BulbState(true, "color_temp", ColorTemp: 275);

        var after = Zigbee2MqttPayloads.ParseBulbState("""{"brightness":100}""", before);

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_state_message_with_hs_color_is_read()
    {
        var state = Zigbee2MqttPayloads.ParseBulbState("""{"state":"ON","color_mode":"hs","color":{"hue":210,"saturation":65}}""", null);

        Assert.Equal(new BulbState(true, "hs", Hue: 210, Saturation: 65), state);
    }

    [Theory]
    [InlineData("color_temp", """{"color_temp":275,"transition":3}""")]
    [InlineData("xy", """{"color":{"x":0.4,"y":0.38},"transition":3}""")]
    [InlineData("hs", """{"color":{"hue":30,"saturation":80},"transition":3}""")]
    public void A_restore_uses_the_saved_color_mode(string mode, string expected)
    {
        var saved = new BulbState(true, mode, X: 0.4, Y: 0.38, Hue: 30, Saturation: 80, ColorTemp: 275);

        Assert.Equal(expected, Zigbee2MqttPayloads.Restore(saved, Fade));
    }

    [Fact]
    public void A_restore_without_a_usable_color_is_null()
    {
        Assert.Null(Zigbee2MqttPayloads.Restore(new BulbState(true, "xy"), Fade));
    }

    [Fact]
    public void A_restore_never_turns_a_bulb_on_or_off()
    {
        var payload = Zigbee2MqttPayloads.Restore(new BulbState(false, "color_temp", ColorTemp: 300), Fade)!;

        Assert.DoesNotContain("\"state\"", payload);
    }
}
