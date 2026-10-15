using System.Text.Json;
using Microsoft.Extensions.Options;
using PlexLightSync.Color;
using PlexLightSync.Lights;
using PlexLightSync.Plex;
using PlexLightSync.Video;

namespace PlexLightSync.Sync;

/// <summary>Makes the sconces follow the video. docs/plan.md describes the rules.</summary>
/// <remarks>
/// One loop owns all the decisions. Every <see cref="Tick"/>, it handles the inbox, sends a due color,
/// and checks the restore timer. A frame pump runs beside it: it reads ffmpeg's frames, picks their
/// colors, and queues them in the scheduler, at most <see cref="ReadAhead"/> ahead of the playback.
/// </remarks>
public sealed class SyncEngine : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ReadAhead = TimeSpan.FromSeconds(30);

    /// <summary>The engine loop touches this file every 10 s. The container healthcheck reads its age.</summary>
    public const string HeartbeatFile = "/tmp/plex-light-sync.alive";
    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    static readonly TimeSpan LastRetry = TimeSpan.FromSeconds(60);

    readonly SyncInbox _inbox;
    readonly PlaybackTracker _playback;
    readonly IMediaFileLookup _files;
    readonly IFrameSource _frames;
    readonly ColorPicker _picker;
    readonly ILights _lights;
    readonly HomeAssistantEntities _homeAssistant;
    readonly SavedColors _savedColors;
    readonly SyncOptions _settings;
    readonly TimeProvider _time;
    readonly ILogger<SyncEngine> _logger;
    readonly ColorScheduler _scheduler = new();
    readonly ColorSender _sender;
    readonly long _createdAt;

    PlayingItem? _item;   // what plays, or is paused, now
    MediaFile? _file;
    IFrameReader? _reader;
    CancellationTokenSource? _pumpStop;
    Task<int>? _pump;     // its result is the number of frames that it read
    bool _failed;
    TimeSpan _retryDelay = FirstRetry;
    TimeSpan? _retryAt;
    IReadOnlyDictionary<string, BulbState>? _saved;
    TimeSpan? _restoreAt;
    TimeSpan? _lastHeartbeat;

    public SyncEngine(
        SyncInbox inbox,
        PlaybackTracker playback,
        IMediaFileLookup files,
        IFrameSource frames,
        ColorPicker picker,
        ILights lights,
        HomeAssistantEntities homeAssistant,
        SavedColors savedColors,
        IOptions<SyncOptions> options,
        TimeProvider time,
        ILogger<SyncEngine> logger)
    {
        _inbox = inbox;
        _playback = playback;
        _files = files;
        _frames = frames;
        _picker = picker;
        _lights = lights;
        _homeAssistant = homeAssistant;
        _savedColors = savedColors;
        _settings = options.Value;
        _time = time;
        _logger = logger;
        _sender = new ColorSender(_settings.MinGap);
        _createdAt = time.GetTimestamp();
    }

    // a steady clock for the timers
    TimeSpan Now => _time.GetElapsedTime(_createdAt);

    SyncStatus Status =>
        _failed ? SyncStatus.Error
        : !_homeAssistant.MovieSyncOn || _item is null ? SyncStatus.Idle
        : !_playback.IsPlaying ? SyncStatus.Paused
        : _lights.AnyOn ? SyncStatus.Syncing
        : SyncStatus.Idle;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // subscribe before the mqtt bus connects, so that the saved colors arrive at the first connect
        _homeAssistant.MovieSyncChanged += on =>
        {
            _inbox.Post(new MovieSyncSwitched(on));
            return Task.CompletedTask;
        };
        await _savedColors.SubscribeAsync(saved =>
        {
            _inbox.Post(new SavedColorsLoaded(saved));
            return Task.CompletedTask;
        }, cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        try
        {
            do
            {
                try
                {
                    while (_inbox.Reader.TryRead(out var message))
                        await HandleAsync(message, stoppingToken);
                    await TickAsync(stoppingToken);
                    Heartbeat();
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // one failed step must not stop the service
                    _logger.LogError(ex, "Sync step failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the service stops
        }
        finally
        {
            await StopReaderAsync();
        }
    }

    internal async Task HandleAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case PlaybackStarted started:
                await StopReaderAsync();
                _item = started.Item;
                _file = null;
                ClearFailure();
                _restoreAt = null; // a new item within the restore delay (autoplay) keeps the saved colors
                if (started.IsPlaying)
                    await StartReaderAsync(started.Position, cancellationToken);
                break;

            case PlaybackResumed resumed:
                await StartReaderAsync(resumed.Position, cancellationToken);
                break;

            case PlaybackSeeked seeked:
                await StartReaderAsync(seeked.Position, cancellationToken);
                break;

            case PlaybackPaused:
                await StopReaderAsync(); // the sconces hold their color
                break;

            case PlaybackStopped:
                await StopReaderAsync();
                _item = null;
                _file = null;
                ClearFailure();
                if (_saved is not null)
                    _restoreAt = Now + _settings.RestoreDelay;
                break;

            case MovieSyncSwitched { On: false }:
                await StopReaderAsync();
                await RestoreAsync(cancellationToken);
                break;

            case MovieSyncSwitched { On: true }:
                if (_item is not null && _playback.IsPlaying && _playback.Position is { } position)
                    await StartReaderAsync(position, cancellationToken);
                break;

            case SavedColorsLoaded loaded:
                _saved = loaded.Saved;
                // colors saved before a restart, and nothing plays: put them back after the delay
                if (_saved is not null && _item is null && _restoreAt is null)
                    _restoreAt = Now + _settings.RestoreDelay;
                break;
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        CheckPump();
        if (_retryAt is { } retryAt && Now >= retryAt && _item is not null && _playback.IsPlaying && _playback.Position is { } current)
            await StartReaderAsync(current, cancellationToken);

        if (_reader is not null && _playback.Position is { } position)
        {
            switch (_scheduler.TakeDue(position + _settings.SendOffset))
            {
                case { Color: { } color }:
                    _sender.Offer(color);
                    break;
                case { }:
                    _sender.Cancel(); // a black or colorless frame: hold, and drop an older color that still waits
                    break;
            }
        }

        if (_homeAssistant.MovieSyncOn && _lights.AnyOn && _sender.TakeReady(Now) is { } ready)
        {
            if (_saved is null)
            {
                _saved = _lights.States;
                await _savedColors.SaveAsync(_saved, cancellationToken);
                _logger.LogInformation("Saved the colors of {Count} bulbs", _saved.Count);
            }
            await _lights.SetColorAsync(ready, _settings.Fade, cancellationToken);
            _logger.LogDebug("Color: hue {Hue:0}°, saturation {Saturation:0.00}", ready.Hue, ready.Saturation);
        }

        if (_restoreAt is { } restoreAt && Now >= restoreAt)
            await RestoreAsync(cancellationToken);

        await _homeAssistant.SetStatusAsync(Status, cancellationToken);
    }

    async Task StartReaderAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        await StopReaderAsync();
        _retryAt = null;
        if (!_homeAssistant.MovieSyncOn || _item is null)
            return;

        try
        {
            _file ??= await _files.GetMediaFileAsync(_item, cancellationToken);
            _reader = _frames.Start(_file, position);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail($"cannot start reading: {ex.Message}");
            return;
        }

        _logger.LogInformation("Reading {PartKey} ({Range}) from {Position}", _file.PartKey, _file.IsHdr ? "hdr" : "sdr", position.ToClock());
        _pumpStop = new CancellationTokenSource();
        _pump = PumpAsync(_reader, _pumpStop.Token);
    }

    async Task<int> PumpAsync(IFrameReader reader, CancellationToken cancellationToken)
    {
        var count = 0;
        try
        {
            await foreach (var frame in reader.ReadFramesAsync(cancellationToken))
            {
                _scheduler.Add(frame.Position, _picker.Pick(frame));
                count++;
                // while the pump waits here, ffmpeg's output pipe fills, and ffmpeg waits too
                while (_scheduler.Horizon - _playback.Position > ReadAhead)
                    await Task.Delay(Tick, _time, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopped on purpose
        }
        return count;
    }

    // looks at the pump from the engine loop, so that the failure state stays on one thread
    void CheckPump()
    {
        if (_pump is { IsCompleted: true } pump)
        {
            _pump = null;
            if (pump.IsFaulted)
            {
                Fail($"reading frames failed: {pump.Exception.GetBaseException().Message}");
                return;
            }
            if (pump.Result == 0 && !(_pumpStop?.IsCancellationRequested ?? true))
            {
                Fail("ffmpeg delivered no frames");
                return;
            }
        }

        // frames arrived from the current reader, so the problem is over
        if (_failed && _scheduler.Horizon is not null)
        {
            _logger.LogInformation("Frames arrive again");
            ClearFailure();
        }
    }

    async Task StopReaderAsync()
    {
        if (_pumpStop is not null)
            await _pumpStop.CancelAsync();
        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The frame pump stopped with an error");
            }
        }
        if (_reader is not null)
            await _reader.DisposeAsync();

        _pumpStop?.Dispose();
        (_pumpStop, _pump, _reader) = (null, null, null);
        _scheduler.Clear();
    }

    async Task RestoreAsync(CancellationToken cancellationToken)
    {
        _restoreAt = null;
        _sender.Reset();
        if (_saved is not { } saved)
            return;

        await _lights.RestoreAsync(saved, _settings.RestoreFade, cancellationToken);
        await _savedColors.ClearAsync(cancellationToken);
        _saved = null;
        _logger.LogInformation("Restored the saved colors");
    }

    void Heartbeat()
    {
        if (_lastHeartbeat is { } last && Now - last < HeartbeatInterval)
            return;
        _lastHeartbeat = Now;
        try
        {
            File.WriteAllText(HeartbeatFile, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Cannot write the heartbeat file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Cannot write the heartbeat file");
        }
    }

    void Fail(string reason)
    {
        _failed = true;
        _retryAt = Now + _retryDelay;
        _logger.LogWarning("Sync problem, retrying in {Delay} s: {Reason}", _retryDelay.TotalSeconds, reason);
        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, LastRetry.Ticks));
    }

    void ClearFailure()
    {
        _failed = false;
        _retryAt = null;
        _retryDelay = FirstRetry;
    }
}
