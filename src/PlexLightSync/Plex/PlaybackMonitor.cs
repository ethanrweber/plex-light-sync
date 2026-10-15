using System.Text.Json;
using Microsoft.Extensions.Options;
using PlexLightSync.Sync;

namespace PlexLightSync.Plex;

/// <summary>
/// Polls Plex every second, follows the configured player, logs its playback events, and passes them
/// to the sync engine.
/// </summary>
public sealed class PlaybackMonitor(
    PlexClient plex,
    PlaybackTracker tracker,
    SyncInbox inbox,
    IOptions<PlexOptions> options,
    TimeProvider time,
    ILogger<PlaybackMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(3);

    readonly HashSet<string> _seenPlayers = [];
    bool _plexDown;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var playerId = options.Value.PlayerId;
        logger.LogInformation("Following Plex player {PlayerId}", playerId);

        using var timer = new PeriodicTimer(PollInterval, time);
        do
        {
            var sessions = await TryGetSessionsAsync(stoppingToken);
            if (sessions is null)
                continue; // keep the current state until Plex answers again

            LogNewPlayers(sessions);
            var report = sessions.FirstOrDefault(s => s.PlayerId == playerId && s.IsSyncable)?.ToReport();
            if (tracker.Observe(report) is { } playbackEvent)
            {
                Log(playbackEvent);
                inbox.Post(playbackEvent);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task<IReadOnlyList<PlexSession>?> TryGetSessionsAsync(CancellationToken stoppingToken)
    {
        try
        {
            var sessions = await plex.GetSessionsAsync(stoppingToken);
            if (_plexDown)
            {
                _plexDown = false;
                logger.LogInformation("Plex answers again");
            }
            return sessions;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                   && !stoppingToken.IsCancellationRequested)
        {
            if (!_plexDown)
            {
                _plexDown = true;
                logger.LogWarning("Plex does not answer, retrying every second: {Error}", ex.Message);
            }
            return null;
        }
    }

    // logs each player once, so that its machine id is easy to find for the settings
    void LogNewPlayers(IReadOnlyList<PlexSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (_seenPlayers.Add(session.PlayerId))
                logger.LogInformation("Plex player seen: {PlayerTitle} ({PlayerId})", session.PlayerTitle, session.PlayerId);
        }
    }

    void Log(PlaybackEvent playbackEvent)
    {
        switch (playbackEvent)
        {
            case PlaybackStarted started:
                logger.LogInformation(
                    "Started {RatingKey} part {PartId} at {Position}, {State}",
                    started.Item.RatingKey, started.Item.PartId, started.Position.ToClock(), started.IsPlaying ? "playing" : "paused");
                break;
            case PlaybackPaused paused:
                logger.LogInformation("Paused at {Position} ({State})", paused.Position.ToClock(), paused.State);
                break;
            case PlaybackResumed resumed:
                logger.LogInformation("Resumed at {Position}", resumed.Position.ToClock());
                break;
            case PlaybackSeeked seeked:
                logger.LogInformation("Skip to {Position} ({Jump})", seeked.Position.ToClock(), seeked.Jump.ToSignedSeconds());
                break;
            case PositionCorrected corrected:
                logger.LogInformation("Report at {Position}, estimate off by {Drift}", corrected.Position.ToClock(), corrected.Drift.ToSignedSeconds());
                break;
            case PlaybackStopped:
                logger.LogInformation("Stopped");
                break;
        }
    }
}
