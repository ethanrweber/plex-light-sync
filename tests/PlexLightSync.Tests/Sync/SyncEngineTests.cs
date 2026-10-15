using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PlexLightSync.Color;
using PlexLightSync.Lights;
using PlexLightSync.Plex;
using PlexLightSync.Sync;
using PlexLightSync.Tests.Lights;
using PlexLightSync.Video;

namespace PlexLightSync.Tests.Sync;

/// <summary>
/// Drives the engine step by step: the tests feed playback events and run ticks, with a fake clock,
/// fake lights, and a fake frame source. Nothing runs in the background.
/// </summary>
public sealed class SyncEngineTests
{
    static readonly PlayingItem Movie = new("100", 1);
    static readonly PlayingItem NextEpisode = new("101", 2);
    static readonly BulbState WhiteOn = new(true, "color_temp", ColorTemp: 275);

    readonly FakeTimeProvider _time = new();
    readonly FakeMqttBus _bus = new();
    readonly FakeLights _lights = new();
    readonly FakeFrames _frames = new();
    readonly FakeFileLookup _files = new();
    readonly PlaybackTracker _playback;
    readonly HomeAssistantEntities _homeAssistant;
    readonly SyncEngine _engine;

    public SyncEngineTests()
    {
        // no report delay, so that positions in the tests are exact
        _playback = new PlaybackTracker(_time, seekThreshold: TimeSpan.FromSeconds(3), reportDelay: TimeSpan.Zero);
        _homeAssistant = new HomeAssistantEntities(_bus, NullLogger<HomeAssistantEntities>.Instance);
        _engine = new SyncEngine(
            new SyncInbox(), _playback, _files, _frames,
            new ColorPicker(Options.Create(new ColorOptions())), _lights, _homeAssistant, new SavedColors(_bus),
            Options.Create(new SyncOptions()), _time, NullLogger<SyncEngine>.Instance);
        _lights.States["bulb-1"] = WhiteOn;
        _lights.States["bulb-2"] = WhiteOn;
    }

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    static VideoFrame Frame(double seconds, byte r, byte g, byte b)
    {
        var pixels = new byte[VideoFrame.ByteCount];
        for (var i = 0; i < pixels.Length; i += 3)
            (pixels[i], pixels[i + 1], pixels[i + 2]) = (r, g, b);
        return new VideoFrame(S(seconds), pixels);
    }

    static VideoFrame Red(double seconds) => Frame(seconds, 255, 0, 0);
    static VideoFrame Blue(double seconds) => Frame(seconds, 0, 0, 255);
    static VideoFrame Black(double seconds) => Frame(seconds, 0, 0, 0);

    CancellationToken Token => TestContext.Current.CancellationToken;

    // feeds a player report through the real tracker, and its event to the engine
    async Task ReportAsync(double seconds, PlayerState state = PlayerState.Playing, PlayingItem? item = null)
    {
        if (_playback.Observe(new PlayerReport(item ?? Movie, S(seconds), state)) is { } playbackEvent)
            await _engine.HandleAsync(playbackEvent, Token);
    }

    async Task StopAsync()
    {
        if (_playback.Observe(null) is { } playbackEvent)
            await _engine.HandleAsync(playbackEvent, Token);
    }

    async Task AdvanceAsync(double seconds)
    {
        _time.Advance(S(seconds));
        await _engine.TickAsync(Token);
    }

    async Task SwitchMovieSyncAsync(bool on)
    {
        await _bus.DeliverAsync("plex-light-sync/movie_sync/set", on ? "ON" : "OFF");
        await _engine.HandleAsync(new MovieSyncSwitched(on), Token);
    }

    async Task StartHomeAssistantAsync() => await _homeAssistant.StartAsync(Token);

    // the entities start idle, and publish only changes
    string LastStatus => _bus.PayloadsFor("plex-light-sync/status").LastOrDefault() ?? "idle";

    [Fact]
    public async Task A_start_saves_the_bulbs_and_sends_the_color_on_screen_now()
    {
        _frames.Frames.AddRange([Red(58), Blue(70)]);

        await ReportAsync(60);
        await _engine.TickAsync(Token);

        Assert.Equal([S(60)], _frames.Starts);
        var (color, fade) = Assert.Single(_lights.Colors);
        Assert.Equal(0, color.Hue, precision: 0);
        Assert.Equal(S(2), fade);
        Assert.Contains(WhiteOn, SavedColors.Parse(_bus.PayloadsFor(SavedColors.Topic).Single())!.Values);
        Assert.Equal("syncing", LastStatus);
    }

    [Fact]
    public async Task A_later_frame_waits_until_its_moment()
    {
        _frames.Frames.AddRange([Red(58), Blue(70)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(9.5); // position 69.5, plus the send offset: 69.8
        Assert.Single(_lights.Colors);

        await AdvanceAsync(0.3); // position 69.8, plus the offset: 70.1
        Assert.Equal(240, _lights.Colors.Last().Color.Hue, precision: 0);
    }

    [Fact]
    public async Task Colors_keep_the_minimum_gap()
    {
        _frames.Frames.AddRange([Red(60), Blue(61)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(1); // blue is due, but red went out 1 s ago
        Assert.Single(_lights.Colors);

        await AdvanceAsync(1);
        Assert.Equal(2, _lights.Colors.Count);
    }

    [Fact]
    public async Task Nothing_changes_while_the_sconces_are_off()
    {
        _lights.AnyOn = false;
        _frames.Frames.Add(Red(60));

        await ReportAsync(60);
        await _engine.TickAsync(Token);

        Assert.Empty(_lights.Colors);
        Assert.Empty(_bus.PayloadsFor(SavedColors.Topic));
        Assert.Equal("idle", LastStatus);
    }

    [Fact]
    public async Task A_black_frame_holds_the_last_color()
    {
        _frames.Frames.AddRange([Red(60), Black(65)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(10);

        Assert.Single(_lights.Colors);
    }

    [Fact]
    public async Task A_pause_holds_and_a_resume_reads_again_from_the_paused_position()
    {
        _frames.Frames.Add(Red(60));
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        _time.Advance(S(5));
        await ReportAsync(65, PlayerState.Paused);
        await _engine.TickAsync(Token);
        Assert.Equal("paused", LastStatus);

        await ReportAsync(65);

        Assert.Equal([S(60), S(65)], _frames.Starts);
    }

    [Fact]
    public async Task A_skip_reads_again_from_the_new_position()
    {
        _frames.Frames.Add(Red(60));
        await ReportAsync(60);

        _time.Advance(S(10));
        await ReportAsync(400);

        Assert.Equal([S(60), S(400)], _frames.Starts);
    }

    [Fact]
    public async Task Turning_movie_sync_off_restores_at_once()
    {
        await StartHomeAssistantAsync();
        _frames.Frames.Add(Red(60));
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await SwitchMovieSyncAsync(false);

        var restored = Assert.Single(_lights.Restores);
        Assert.Equal(WhiteOn, restored.Saved["bulb-1"]);
        Assert.Equal(S(3), restored.Fade);
        Assert.Equal("", _bus.PayloadsFor(SavedColors.Topic).Last()); // the saved colors are gone
        Assert.Equal(1, _frames.Disposed);
    }

    [Fact]
    public async Task A_stop_restores_after_the_delay()
    {
        _frames.Frames.Add(Red(60));
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await StopAsync();
        await AdvanceAsync(29);
        Assert.Empty(_lights.Restores);

        await AdvanceAsync(1);
        Assert.Single(_lights.Restores);
    }

    [Fact]
    public async Task An_autoplayed_episode_within_the_delay_keeps_the_saved_colors()
    {
        _frames.Frames.Add(Red(0));
        await ReportAsync(1500);
        await _engine.TickAsync(Token);

        await StopAsync();
        await AdvanceAsync(5);
        _lights.States["bulb-1"] = new BulbState(true, "hs", Hue: 0, Saturation: 100); // movie colors now
        await ReportAsync(0, item: NextEpisode);
        await AdvanceAsync(60);

        Assert.Empty(_lights.Restores);
        Assert.Single(_bus.PayloadsFor(SavedColors.Topic)); // saved once, before the first episode
    }

    [Fact]
    public async Task Colors_saved_before_a_restart_come_back_when_nothing_plays()
    {
        var saved = new Dictionary<string, BulbState> { ["bulb-1"] = WhiteOn };

        await _engine.HandleAsync(new SavedColorsLoaded(saved), Token);
        await AdvanceAsync(30);

        Assert.Equal(WhiteOn, Assert.Single(_lights.Restores).Saved["bulb-1"]);
    }

    [Fact]
    public async Task A_failed_file_lookup_shows_an_error_and_tries_again()
    {
        _files.Failures = 1;
        _frames.Frames.Add(Red(60));

        await ReportAsync(60);
        await _engine.TickAsync(Token);
        Assert.Equal("error", LastStatus);
        Assert.Empty(_frames.Starts);

        await AdvanceAsync(5);
        await _engine.TickAsync(Token);

        Assert.Single(_frames.Starts);
        Assert.Equal("syncing", LastStatus);
    }

    sealed class FakeLights : ILights
    {
        public bool AnyOn { get; set; } = true;
        public Dictionary<string, BulbState> States { get; } = [];
        public List<(HsColor Color, TimeSpan Fade)> Colors { get; } = [];
        public List<(IReadOnlyDictionary<string, BulbState> Saved, TimeSpan Fade)> Restores { get; } = [];

        IReadOnlyDictionary<string, BulbState> ILights.States => new Dictionary<string, BulbState>(States);

        public Task SetColorAsync(HsColor color, TimeSpan fade, CancellationToken cancellationToken)
        {
            Colors.Add((color, fade));
            return Task.CompletedTask;
        }

        public Task RestoreAsync(IReadOnlyDictionary<string, BulbState> saved, TimeSpan fade, CancellationToken cancellationToken)
        {
            Restores.Add((saved, fade));
            return Task.CompletedTask;
        }
    }

    // gives every reader the same frames at once, as ffmpeg does with a fast start
    sealed class FakeFrames : IFrameSource
    {
        public List<VideoFrame> Frames { get; } = [];
        public List<TimeSpan> Starts { get; } = [];
        public int Disposed { get; private set; }

        public IFrameReader Start(MediaFile file, TimeSpan position)
        {
            Starts.Add(position);
            return new Reader(this);
        }

        sealed class Reader(FakeFrames source) : IFrameReader
        {
            public async IAsyncEnumerable<VideoFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var frame in source.Frames.ToList())
                    yield return frame;
                await Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                source.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    sealed class FakeFileLookup : IMediaFileLookup
    {
        public int Failures { get; set; }

        public Task<MediaFile> GetMediaFileAsync(PlayingItem item, CancellationToken cancellationToken)
        {
            if (Failures-- > 0)
                throw new HttpRequestException("plex is down");
            return Task.FromResult(new MediaFile(item.PartId, $"/library/parts/{item.PartId}/1/file.mkv", IsHdr: false));
        }
    }
}
