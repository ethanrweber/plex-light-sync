using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PlexLightSync.Plex;

/// <summary>
/// Polls Plex every second, follows the configured player, and logs its playback events.
/// </summary>
/// <remarks>For now this only logs. A later step adds the sync engine, which consumes the events.</remarks>
public sealed class PlaybackMonitor(
    PlexClient plex,
    IOptions<PlexOptions> options,
    TimeProvider time,
    ILogger<PlaybackMonitor> logger) : BackgroundService
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(3);

    readonly HashSet<string> _seenPlayers = [];
    bool _plexDown;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var playerId = options.Value.PlayerId;
        var tracker = new PlaybackTracker(time, SeekThreshold, reportDelay: PollInterval / 2);
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
                await LogAsync(playbackEvent, stoppingToken);
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

    async Task LogAsync(PlaybackEvent playbackEvent, CancellationToken stoppingToken)
    {
        switch (playbackEvent)
        {
            case PlaybackStarted started:
                logger.LogInformation(
                    "Started {RatingKey} part {PartId} at {Position}, {State}, {File}",
                    started.Item.RatingKey, started.Item.PartId, started.Position.ToClock(),
                    started.IsPlaying ? "playing" : "paused", await DescribeFileAsync(started.Item, stoppingToken));
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

    async Task<string> DescribeFileAsync(PlayingItem item, CancellationToken stoppingToken)
    {
        try
        {
            var file = await plex.GetMediaFileAsync(item, stoppingToken);
            return $"{(file.IsHdr ? "hdr" : "sdr")} {file.PartKey}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                   && !stoppingToken.IsCancellationRequested)
        {
            return $"file lookup failed: {ex.Message}";
        }
    }
}
