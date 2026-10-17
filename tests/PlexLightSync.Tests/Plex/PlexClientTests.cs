using System.Net;
using System.Text;
using PlexLightSync.Plex;

namespace PlexLightSync.Tests.Plex;

public sealed class PlexClientTests
{
    static readonly PlexOptions Options = new()
    {
        Url = "https://plex.example:32400",
        Token = "secret-token",
        ClientId = "client-1",
        PlayerId = "tv-1",
    };

    // shaped like Plex's /status/sessions JSON, with made-up values
    const string Sessions = """
        {
          "MediaContainer": {
            "size": 4,
            "Metadata": [
              {
                "type": "movie", "ratingKey": "100", "viewOffset": 1363540, "duration": 7402195,
                "Media": [{ "id": 58, "Part": [{ "id": 58, "key": "/library/parts/58/1700000000/file.mkv" }] }],
                "Player": { "machineIdentifier": "tv-1", "title": "Living Room TV", "state": "playing" }
              },
              {
                "type": "clip", "ratingKey": "200", "viewOffset": 1000,
                "Media": [{ "id": 7, "Part": [{ "id": 7, "key": "/library/parts/7/1/file.mp4" }] }],
                "Player": { "machineIdentifier": "phone-1", "title": "Phone", "state": "paused" }
              },
              {
                "type": "episode", "ratingKey": "300", "live": 1,
                "Player": { "machineIdentifier": "tv-2", "title": "Bedroom TV", "state": "buffering" }
              },
              {
                "type": "episode", "ratingKey": "400",
                "Player": { "machineIdentifier": "tv-3", "title": "Office", "state": "stopped" }
              }
            ]
          }
        }
        """;

    // shaped like Plex's /library/metadata/<ratingKey> JSON: two versions of one movie
    const string Metadata = """
        {
          "MediaContainer": {
            "size": 1,
            "Metadata": [{
              "type": "movie", "ratingKey": "100",
              "Media": [
                { "id": 1, "Part": [{ "id": 11, "key": "/library/parts/11/1/file.mkv",
                  "Stream": [{ "streamType": 1, "codec": "h264", "width": 1920 }, { "streamType": 2, "codec": "aac" }] }] },
                { "id": 2, "Part": [{ "id": 22, "key": "/library/parts/22/1/file.mkv",
                  "Stream": [{ "streamType": 1, "codec": "hevc", "colorTrc": "smpte2084", "DOVIPresent": true, "width": 3840 }] }] }
              ]
            }]
          }
        }
        """;

    static (PlexClient Client, StubHandler Handler) CreateClient(string json)
    {
        var handler = new StubHandler(json);
        var http = new HttpClient(handler);
        PlexClient.Configure(http, Options);
        return (new PlexClient(http), handler);
    }

    [Fact]
    public async Task Sessions_are_parsed_for_every_player()
    {
        var (client, _) = CreateClient(Sessions);

        var sessions = await client.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, sessions.Count);
        var movie = sessions[0];
        Assert.Equal("tv-1", movie.PlayerId);
        Assert.Equal("Living Room TV", movie.PlayerTitle);
        Assert.Equal(new PlayingItem("100", 58), movie.Item);
        Assert.Equal(TimeSpan.FromMilliseconds(1363540), movie.Offset);
        Assert.Equal(PlayerState.Playing, movie.State);
    }

    [Fact]
    public async Task Only_library_movies_and_episodes_with_a_known_state_are_syncable()
    {
        var (client, _) = CreateClient(Sessions);

        var sessions = await client.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([true, false, false, false], sessions.Select(s => s.IsSyncable));
        Assert.True(sessions[2].IsLive);
        Assert.Null(sessions[3].State);
        Assert.Null(sessions[3].ToReport());
    }

    [Fact]
    public async Task The_file_comes_from_the_part_that_plays_with_its_hdr_flag_and_width()
    {
        var (client, handler) = CreateClient(Metadata);
        var token = TestContext.Current.CancellationToken;

        var hdr = await client.GetMediaFileAsync(new PlayingItem("100", 22), token);
        var sdr = await client.GetMediaFileAsync(new PlayingItem("100", 11), token);

        Assert.Equal(new MediaFile(22, "/library/parts/22/1/file.mkv", IsHdr: true, Width: 3840), hdr);
        Assert.Equal(new MediaFile(11, "/library/parts/11/1/file.mkv", IsHdr: false, Width: 1920), sdr);
        Assert.Equal("/library/metadata/100", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task All_files_of_an_item_are_listed()
    {
        var (client, _) = CreateClient(Metadata);

        var files = await client.GetMediaFilesAsync("100", TestContext.Current.CancellationToken);

        Assert.Equal([11L, 22L], files.Select(file => file.PartId));
    }

    [Fact]
    public async Task A_missing_part_is_an_error()
    {
        var (client, _) = CreateClient(Metadata);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetMediaFileAsync(new PlayingItem("100", 99), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_token_goes_in_a_header_and_never_in_the_url()
    {
        var (client, handler) = CreateClient(Sessions);

        await client.GetSessionsAsync(TestContext.Current.CancellationToken);

        var request = handler.LastRequest!;
        Assert.Equal("https://plex.example:32400/status/sessions", request.RequestUri!.ToString());
        Assert.Equal(["secret-token"], request.Headers.GetValues("X-Plex-Token"));
        Assert.Equal(["client-1"], request.Headers.GetValues("X-Plex-Client-Identifier"));
    }

    sealed class StubHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
