namespace PlexLightSync.Plex;

public enum PlayerState { Playing, Paused, Buffering }

/// <summary>A playing file: the library item and the part (file) of it.</summary>
public sealed record PlayingItem(string RatingKey, long PartId);

/// <summary>What the player reported at one poll.</summary>
public sealed record PlayerReport(PlayingItem Item, TimeSpan Offset, PlayerState State);

/// <summary>One file (part) of a library item. <see cref="PartKey"/> is its path on the Plex server.</summary>
/// <param name="Width">The width of the video in pixels. 0 if Plex does not know it.</param>
public sealed record MediaFile(long PartId, string PartKey, bool IsHdr, int Width = 0);

/// <summary>One playback session on the Plex server.</summary>
public sealed record PlexSession(
    string PlayerId,
    string PlayerTitle,
    string Type,
    bool IsLive,
    PlayingItem Item,
    TimeSpan Offset,
    PlayerState? State)
{
    /// <summary>
    /// True for a movie or an episode from a library. Trailers and extras ("clip"), music ("track"),
    /// live tv, and unknown player states do not count.
    /// </summary>
    public bool IsSyncable => Type is "movie" or "episode" && !IsLive && State is not null;

    public PlayerReport? ToReport() => State is { } state ? new PlayerReport(Item, Offset, state) : null;

    internal static PlexSession From(PlexMetadata metadata)
    {
        // a session has one media and one part: the ones that play
        var part = metadata.Media?.FirstOrDefault()?.Part?.FirstOrDefault();
        return new PlexSession(
            PlayerId: metadata.Player?.MachineIdentifier ?? "",
            PlayerTitle: metadata.Player?.Title ?? "",
            Type: metadata.Type ?? "",
            IsLive: metadata.IsLive,
            Item: new PlayingItem(metadata.RatingKey ?? "", part?.Id ?? 0),
            Offset: TimeSpan.FromMilliseconds(metadata.ViewOffset ?? 0),
            State: metadata.Player?.State switch
            {
                "playing" => PlayerState.Playing,
                "paused" => PlayerState.Paused,
                "buffering" => PlayerState.Buffering,
                _ => null,
            });
    }
}
