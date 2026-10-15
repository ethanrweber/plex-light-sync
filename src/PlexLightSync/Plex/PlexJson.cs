using System.Text.Json;

namespace PlexLightSync.Plex;

// The parts of Plex's JSON responses that this service reads. Plex sends many more fields; the parser
// ignores them. Property names match case-insensitively (JsonSerializerDefaults.Web).

internal sealed record PlexResponse(PlexContainer MediaContainer);

internal sealed record PlexContainer(List<PlexMetadata>? Metadata);

internal sealed record PlexMetadata(
    string? Type,
    string? RatingKey,
    long? ViewOffset,
    JsonElement? Live,
    PlexPlayer? Player,
    List<PlexMedia>? Media)
{
    /// <summary>Live tv sessions carry a "live" flag. Accept it as a number, a string or a bool.</summary>
    public bool IsLive => Live?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => Live.Value.GetInt32() == 1,
        JsonValueKind.String => Live.Value.GetString() == "1",
        _ => false,
    };
}

internal sealed record PlexPlayer(string? MachineIdentifier, string? Title, string? State);

internal sealed record PlexMedia(long Id, List<PlexPart>? Part);

internal sealed record PlexPart(long Id, string? Key, List<PlexStream>? Stream);

internal sealed record PlexStream(int StreamType, string? ColorTrc)
{
    public const int Video = 1;
}
