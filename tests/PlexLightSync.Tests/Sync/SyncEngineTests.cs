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

        // blue goes out 1.3 s early: the send offset (0.3 s) and half the fade (1 s), so that the fade is centered on 70
        await AdvanceAsync(8.5); // position 68.5: 69.8
        Assert.Single(_lights.Colors);

        await AdvanceAsync(0.3); // position 68.8: 70.1
        Assert.Equal(240, _lights.Colors.Last().Color.Hue, precision: 0);
    }

    [Fact]
    public async Task Colors_keep_the_minimum_gap()
    {
        _frames.Frames.AddRange([Red(60), Blue(61.5)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(0.2); // blue is due (61.5 - 1.3), but red went out 0.2 s ago
        Assert.Single(_lights.Colors);

        await AdvanceAsync(0.3);
        Assert.Equal(2, _lights.Colors.Count);
    }

    // a 1080p file at 2 frames per second, red from 60 s to 80 s
    void Scene(Func<double, VideoFrame> frameAt)
    {
        _files.Width = 1920;
        for (var t = 60.0; t < 80; t += 0.5)
            _frames.Frames.Add(frameAt(t));
    }

    [Fact]
    public async Task A_short_flash_in_a_scene_does_not_reach_the_lights()
    {
        Scene(t => t is >= 70 and < 71.5 ? Blue(t) : Red(t)); // 1.5 s of blue
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        for (var i = 0; i < 150; i++)
            await AdvanceAsync(0.1);

        Assert.All(_lights.Colors, sent => Assert.Equal(0, sent.Color.Hue, precision: 0));
    }

    [Fact]
    public async Task A_lasting_change_goes_out_before_the_cut_so_that_the_fade_is_around_it()
    {
        Scene(t => t >= 70 ? Blue(t) : Red(t));
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        while (_lights.Colors.Count < 2 && _playback.Position < S(75))
            await AdvanceAsync(0.1);

        Assert.Equal(240, _lights.Colors[^1].Color.Hue, precision: 0);
        Assert.InRange(_playback.Position!.Value, S(68.7), S(70)); // the 2 s fade starts 0.3 s later and spans the cut
    }

    [Fact]
    public async Task The_first_vote_after_a_start_waits_for_a_full_window()
    {
        _files.Width = 1920;
        _frames.KeepOpen = true; // ffmpeg is still busy: the frames after 62 s have not come yet
        _frames.Frames.AddRange([Red(60), Red(60.5), Red(61), Red(61.5), Red(62)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        Assert.Empty(_lights.Colors); // the vote at 61.3 needs frames up to 64.3
    }

    [Fact]
    public async Task At_the_end_of_the_file_the_vote_takes_the_frames_that_are_left()
    {
        _files.Width = 1920;
        _frames.Frames.AddRange([Red(60), Red(60.5), Red(61), Red(61.5), Red(62)]); // then ffmpeg ends
        await ReportAsync(60);
        await _engine.TickAsync(Token);
        await _engine.TickAsync(Token); // the first tick sees that the pump ended

        Assert.Equal(0, Assert.Single(_lights.Colors).Color.Hue, precision: 0);
    }

    [Fact]
    public async Task A_1080p_file_gives_one_frame_per_gap_and_a_4k_file_only_its_keyframes()
    {
        _files.Width = 1920;
        await ReportAsync(60);

        await StopAsync();
        _files.Width = 3840;
        await ReportAsync(0, item: NextEpisode);

        Assert.Equal([2.0, null], _frames.Rates);
    }

    [Fact]
    public async Task Frames_that_keep_up_do_not_change_the_mode()
    {
        _files.Width = 1920;
        _frames.KeepOpen = true;
        _frames.Frames.AddRange([Red(60), Blue(70), Red(80)]);
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(20);

        Assert.Equal([2.0], _frames.Rates);
    }

    [Fact]
    public async Task A_file_that_decodes_too_slowly_reads_only_keyframes_until_the_next_item()
    {
        _files.Width = 1920;
        _frames.KeepOpen = true;    // ffmpeg runs, but it decodes too slowly to deliver more frames
        _frames.Frames.Add(Red(60));
        await ReportAsync(60);
        await _engine.TickAsync(Token);

        await AdvanceAsync(15);     // 15 s behind: a normal lag after a start can be up to ~10 s
        Assert.Equal([2.0], _frames.Rates);

        await AdvanceAsync(0.1);
        Assert.Equal([2.0, null], _frames.Rates);
        Assert.Equal(75.1, _frames.Starts[^1].TotalSeconds, precision: 3);

        _time.Advance(S(1));
        await ReportAsync(300);     // a skip keeps the keyframes
        Assert.Equal([2.0, null, null], _frames.Rates);

        await StopAsync();
        await ReportAsync(0, item: NextEpisode);
        Assert.Equal([2.0, null, null, 2.0], _frames.Rates);
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
        public List<double?> Rates { get; } = [];
        public int Disposed { get; private set; }

        /// <summary>True: after its frames, a reader waits instead of ending, like an ffmpeg that is still busy.</summary>
        public bool KeepOpen { get; set; }

        public IFrameReader Start(MediaFile file, TimeSpan position, double? framesPerSecond)
        {
            Starts.Add(position);
            Rates.Add(framesPerSecond);
            return new Reader(this);
        }

        sealed class Reader(FakeFrames source) : IFrameReader
        {
            public async IAsyncEnumerable<VideoFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var frame in source.Frames.ToList())
                    yield return frame;
                if (source.KeepOpen)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
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

        /// <summary>0 (unknown) by default: only keyframes, so most tests need no frame rate.</summary>
        public int Width { get; set; }

        public Task<MediaFile> GetMediaFileAsync(PlayingItem item, CancellationToken cancellationToken)
        {
            if (Failures-- > 0)
                throw new HttpRequestException("plex is down");
            return Task.FromResult(new MediaFile(item.PartId, $"/library/parts/{item.PartId}/1/file.mkv", IsHdr: false, Width));
        }
    }
}
