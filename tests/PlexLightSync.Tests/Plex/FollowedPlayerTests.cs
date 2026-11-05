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

    // sessions on the tv (192.168.1.5): plezy, and the official app with a session that Plex still keeps
    static readonly PlayingItem Movie = new("100", 1);
    static readonly PlayingItem OtherMovie = new("200", 2);
    static PlexSession Plezy(PlayerState state, PlayingItem? item = null) => Session("plezy", "192.168.1.5", state) with { Item = item ?? Movie };
    static PlexSession PlexApp(PlayerState state) => Session("plex-app", "192.168.1.5", state) with { Item = OtherMovie };

    [Fact]
    public void A_stop_is_a_stop_even_when_another_app_on_the_device_keeps_a_paused_session()
    {
        var player = Player(address: "192.168.1.5");

        Assert.Equal("plezy", player.Follow([PlexApp(PlayerState.Paused), Plezy(PlayerState.Playing)])?.PlayerId);
        Assert.Null(player.Follow([PlexApp(PlayerState.Paused)])); // plezy stopped
    }

    [Fact]
    public void A_pause_stays_on_the_followed_session_in_any_list_order()
    {
        var player = Player(address: "192.168.1.5");

        player.Follow([Plezy(PlayerState.Playing), PlexApp(PlayerState.Paused)]);

        Assert.Equal("plezy", player.Follow([PlexApp(PlayerState.Paused), Plezy(PlayerState.Paused)])?.PlayerId);
        Assert.Equal("plezy", player.Follow([PlexApp(PlayerState.Paused), Plezy(PlayerState.Playing)])?.PlayerId);
    }

    [Fact]
    public void The_followed_session_stays_while_another_one_also_plays_in_any_list_order()
    {
        var player = Player(address: "192.168.1.5");

        Assert.Equal("plezy", player.Follow([Plezy(PlayerState.Playing), PlexApp(PlayerState.Playing)])?.PlayerId);
        Assert.Equal("plezy", player.Follow([PlexApp(PlayerState.Playing), Plezy(PlayerState.Playing)])?.PlayerId);
    }

    [Fact]
    public void Another_session_on_the_device_takes_over_when_it_plays_and_the_followed_one_does_not()
    {
        var player = Player(address: "192.168.1.5");

        player.Follow([Plezy(PlayerState.Playing)]);

        Assert.Equal("plex-app", player.Follow([Plezy(PlayerState.Paused), PlexApp(PlayerState.Playing)])?.PlayerId);
        Assert.Equal("plex-app", player.Follow([Plezy(PlayerState.Playing), PlexApp(PlayerState.Playing)])?.PlayerId);
    }

    [Fact]
    public void A_paused_session_is_followed_only_after_it_plays()
    {
        var player = Player(address: "192.168.1.5");

        Assert.Null(player.Follow([PlexApp(PlayerState.Paused)]));
        Assert.Equal("plex-app", player.Follow([PlexApp(PlayerState.Playing)])?.PlayerId);
        Assert.Equal(PlayerState.Paused, player.Follow([PlexApp(PlayerState.Paused)])?.State);
    }

    [Fact]
    public void The_next_item_in_the_same_app_is_followed()
    {
        var player = Player(address: "192.168.1.5");
        var next = new PlayingItem("101", 3);

        player.Follow([Plezy(PlayerState.Playing)]);

        Assert.Equal(next, player.Follow([Plezy(PlayerState.Playing, next)])?.Item);
    }

    [Fact]
    public void Only_syncable_sessions_of_the_followed_device_are_followed()
    {
        var player = Player(address: "192.168.1.5");

        Assert.Null(player.Follow([
            Session("plezy", "192.168.1.5", type: "clip"),
            Session("plezy", "192.168.1.5", state: null),
            Session("phone", "192.168.1.70"),
        ]));
    }

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData(" 192.168.1.5 ", true)]
    [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("2001:db8::5", true)]
    [InlineData("192.168.1", false)]     // IPAddress.TryParse reads it as 192.168.0.1
    [InlineData("5", false)]             // ... as 0.0.0.5
    [InlineData("192.168.1.010", false)] // ... as 192.168.1.8 (octal)
    [InlineData("192.168.1.5:32400", false)]
    [InlineData("living-room-tv", false)]
    public void A_setting_must_be_an_address_in_its_usual_form(string text, bool expected)
    {
        Assert.Equal(expected, FollowedPlayer.IsAddress(text));
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
