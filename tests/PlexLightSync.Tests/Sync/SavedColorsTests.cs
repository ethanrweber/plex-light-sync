using PlexLightSync.Lights;
using PlexLightSync.Sync;
using PlexLightSync.Tests.Lights;

namespace PlexLightSync.Tests.Sync;

public sealed class SavedColorsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)] // what mqttnet gives for the empty message that deletes the saved colors
    public void An_empty_message_means_nothing_is_saved(string? payload) =>
        Assert.Null(SavedColors.Parse(payload));

    [Fact]
    public async Task Saved_colors_survive_the_round_trip_through_the_broker()
    {
        var bus = new FakeMqttBus();
        var saved = new SavedColors(bus);
        var states = new Dictionary<string, BulbState> { ["bulb-1"] = new(true, "color_temp", ColorTemp: 275) };

        await saved.SaveAsync(states, TestContext.Current.CancellationToken);

        var message = Assert.Single(bus.Published);
        Assert.True(message.Retain);
        Assert.Equal(states, SavedColors.Parse(message.Payload));
    }
}
