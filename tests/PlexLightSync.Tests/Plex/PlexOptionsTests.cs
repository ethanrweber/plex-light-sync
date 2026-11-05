using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlexLightSync.Plex;

namespace PlexLightSync.Tests.Plex;

public sealed class PlexOptionsTests
{
    [Fact]
    public void Environment_variable_names_and_options_json_names_bind_to_the_same_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PLEX_URL"] = "https://plex.example:32400", // environment variable style
                ["plex_token"] = "secret-token",              // options.json style
                ["Plex_Client_Id"] = "client-1",
                ["PLEX_PLAYER_ID"] = "tv-1",
                ["plex_player_address"] = "192.168.1.5",
            })
            .Build();

        var options = configuration.Get<PlexOptions>()!;

        Assert.Equal("https://plex.example:32400", options.Url);
        Assert.Equal("secret-token", options.Token);
        Assert.Equal("client-1", options.ClientId);
        Assert.Equal("tv-1", options.PlayerId);
        Assert.Equal("192.168.1.5", options.PlayerAddress);
    }

    // the same validation as Program.cs: data annotations, then PlexOptions.Validate
    static PlexOptions Load(string? address, string? id)
    {
        var settings = new Dictionary<string, string?>
        {
            ["plex_url"] = "https://plex.example:32400",
            ["plex_token"] = "secret-token",
            ["plex_client_id"] = "client-1",
            ["plex_player_address"] = address,
            ["plex_player_id"] = id,
        };
        var services = new ServiceCollection();
        services.AddOptions<PlexOptions>()
            .Bind(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .ValidateDataAnnotations();
        return services.BuildServiceProvider().GetRequiredService<IOptions<PlexOptions>>().Value;
    }

    [Theory]
    [InlineData("192.168.1.5", null, "at 192.168.1.5")]
    [InlineData(null, "tv-1", "tv-1")]
    [InlineData("192.168.1.5", "tv-1", "at 192.168.1.5 or tv-1")]
    public void An_address_an_id_or_both_name_the_player(string? address, string? id, string expected)
    {
        var player = new FollowedPlayer(Load(address, id));

        Assert.Equal(expected, player.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")] // the home assistant form can save empty values
    public void Without_an_address_or_an_id_the_settings_error_names_both_keys(string? address, string? id)
    {
        var error = Assert.Throws<OptionsValidationException>(() => Load(address, id));

        Assert.Contains("plex_player_address", error.Message);
        Assert.Contains("plex_player_id", error.Message);
    }

    [Theory]
    [InlineData("living-room-tv")]
    [InlineData("192.168.1.300")]
    [InlineData("192.168.1")] // a short form that IPAddress.TryParse takes as 192.168.0.1
    public void An_address_that_is_not_an_ip_address_is_a_settings_error(string address)
    {
        var error = Assert.Throws<OptionsValidationException>(() => Load(address, null));

        Assert.Contains($"plex_player_address \"{address}\" is not an IP address", error.Message);
    }
}
