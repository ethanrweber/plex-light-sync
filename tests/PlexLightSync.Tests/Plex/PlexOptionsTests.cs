using Microsoft.Extensions.Configuration;
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
            })
            .Build();

        var options = configuration.Get<PlexOptions>()!;

        Assert.Equal("https://plex.example:32400", options.Url);
        Assert.Equal("secret-token", options.Token);
        Assert.Equal("client-1", options.ClientId);
        Assert.Equal("tv-1", options.PlayerId);
    }
}
