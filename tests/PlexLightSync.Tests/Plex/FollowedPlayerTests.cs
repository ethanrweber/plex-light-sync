using System.Net;
using PlexLightSync.Plex;

namespace PlexLightSync.Tests.Plex;

public sealed class FollowedPlayerTests
{
    static PlexSession Session(string id, string? address, PlayerState? state = PlayerState.Playing, string type = "movie") =>
        new(id, "Living Room TV", address is null ? null : IPAddress.Parse(address), type, IsLive: false,
            new PlayingItem("100", 1), TimeSpan.FromMinutes(5), state);

    static FollowedPlayer Player(string address = "", string id = "") => new(new PlexOptions { PlayerAddress = address, PlayerId = id });

    [Fact]
    public void An_address_follows_every_app_on_the_device()
    {
        var player = Player(address: "192.168.1.5");

        Assert.True(player.Matches(Session("plex-app", "192.168.1.5")));
        Assert.True(player.Matches(Session("plezy", "192.168.1.5")));
        Assert.False(player.Matches(Session("plex-app", "192.168.1.70")));
        Assert.False(player.Matches(Session("plex-app", null)));
    }

    [Fact]
    public void An_id_follows_one_app_wherever_it_is()
    {
        var player = Player(id: "plezy");

        Assert.True(player.Matches(Session("plezy", "192.168.1.70")));
        Assert.True(player.Matches(Session("plezy", null)));
        Assert.False(player.Matches(Session("plex-app", "192.168.1.5")));
    }

    [Fact]
    public void With_both_settings_either_one_matches()
    {
        var player = Player(address: "192.168.1.5", id: "phone");

        Assert.True(player.Matches(Session("plezy", "192.168.1.5")));
        Assert.True(player.Matches(Session("phone", "192.168.1.70")));
        Assert.False(player.Matches(Session("laptop", "192.168.1.70")));
    }

    [Fact]
    public void The_address_setting_may_have_spaces_or_the_ipv6_form()
    {
        Assert.True(Player(address: " 192.168.1.5 ").Matches(Session("plezy", "192.168.1.5")));
        Assert.True(Player(address: "::ffff:192.168.1.5").Matches(Session("plezy", "192.168.1.5")));
    }

    [Fact]
    public void The_session_that_plays_wins_over_a_paused_one_on_the_same_device()
    {
        var player = Player(address: "192.168.1.5");
        var stale = Session("plex-app", "192.168.1.5", PlayerState.Paused);
        var playing = Session("plezy", "192.168.1.5", PlayerState.Playing);

        Assert.Same(playing, player.Pick([stale, playing]));
        Assert.Same(stale, player.Pick([stale]));
    }

    [Fact]
    public void Only_syncable_sessions_of_the_followed_device_are_picked()
    {
        var player = Player(address: "192.168.1.5");

        Assert.Null(player.Pick([
            Session("plezy", "192.168.1.5", type: "clip"),
            Session("plezy", "192.168.1.5", state: null),
            Session("phone", "192.168.1.70"),
        ]));
    }

    [Theory]
    [InlineData("192.168.1.5", "", "at 192.168.1.5")]
    [InlineData("", "plezy", "plezy")]
    [InlineData("192.168.1.5", "plezy", "at 192.168.1.5 or plezy")]
    public void The_log_names_what_the_service_follows(string address, string id, string expected)
    {
        Assert.Equal(expected, Player(address, id).ToString());
    }
}
