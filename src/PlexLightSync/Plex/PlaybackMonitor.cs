using System.Net;
using System.Text.Json;
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
    FollowedPlayer player,
    TimeProvider time,
    ILogger<PlaybackMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(3);

    readonly HashSet<(string Id, IPAddress? Address)> _seenPlayers = [];
    bool _plexDown;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Following Plex player {Player}", player);

        using var timer = new PeriodicTimer(PollInterval, time);
        do
        {
            var sessions = await TryGetSessionsAsync(stoppingToken);
            if (sessions is null)
                continue; // keep the current state until Plex answers again

            LogNewPlayers(sessions);
            var report = player.Follow(sessions)?.ToReport();
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

    // logs each player once, and again at a new address, so that its address and machine id are easy
    // to find for the settings
    void LogNewPlayers(IReadOnlyList<PlexSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (_seenPlayers.Add((session.PlayerId, session.PlayerAddress)))
                logger.LogInformation(
                    "Plex player seen: {PlayerTitle} at {PlayerAddress} ({PlayerId})",
                    session.PlayerTitle, session.PlayerAddress?.ToString() ?? "no address", session.PlayerId);
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
