using System.Net.Http.Json;
using System.Text.Json;

namespace PlexLightSync.Plex;

/// <summary>Finds the file of a playing item. Tests replace it with a fake.</summary>
public interface IMediaFileLookup
{
    Task<MediaFile> GetMediaFileAsync(PlayingItem item, CancellationToken cancellationToken);
}

/// <summary>Reads playback sessions and library items from the Plex server.</summary>
public sealed class PlexClient(HttpClient http) : IMediaFileLookup
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sets the server address and the Plex headers. The token goes in a header, not in the URL, so a
    /// logged URL never contains it.
    /// </summary>
    public static void Configure(HttpClient http, PlexOptions options)
    {
        http.BaseAddress = new Uri(options.Url);
        http.Timeout = TimeSpan.FromSeconds(5);
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        http.DefaultRequestHeaders.Add("X-Plex-Product", "plex-light-sync");
        http.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", options.ClientId);
        http.DefaultRequestHeaders.Add("X-Plex-Token", options.Token);
    }

    /// <summary>All playback sessions on the server, for every player.</summary>
    public async Task<IReadOnlyList<PlexSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var response = await http.GetFromJsonAsync<PlexResponse>("/status/sessions", Json, cancellationToken);
        return [.. (response?.MediaContainer.Metadata ?? []).Select(PlexSession.From)];
    }

    /// <summary>
    /// The file that plays. It comes from the library, not from the session: in a transcoded session,
    /// the session describes the transcode.
    /// </summary>
    public async Task<MediaFile> GetMediaFileAsync(PlayingItem item, CancellationToken cancellationToken)
    {
        var files = await GetMediaFilesAsync(item.RatingKey, cancellationToken);
        return files.FirstOrDefault(file => file.PartId == item.PartId)
            ?? throw new InvalidOperationException($"Plex item {item.RatingKey} has no part {item.PartId}.");
    }

    /// <summary>All files of a library item: every part of every version.</summary>
    public async Task<IReadOnlyList<MediaFile>> GetMediaFilesAsync(string ratingKey, CancellationToken cancellationToken)
    {
        var path = "/library/metadata/" + Uri.EscapeDataString(ratingKey);
        var response = await http.GetFromJsonAsync<PlexResponse>(path, Json, cancellationToken);
        return [.. (response?.MediaContainer.Metadata ?? [])
            .SelectMany(metadata => metadata.Media ?? [])
            .SelectMany(media => media.Part ?? [])
            .Where(part => part.Key is not null)
            .Select(part => new MediaFile(part.Id, part.Key!, IsHdr(part), VideoStream(part)?.Width ?? 0))];
    }

    static PlexStream? VideoStream(PlexPart part) => part.Stream?.FirstOrDefault(stream => stream.StreamType == PlexStream.Video);

    // smpte2084 is hdr10 (and the base layer of dolby vision); arib-std-b67 is hlg
    static bool IsHdr(PlexPart part) => VideoStream(part)?.ColorTrc is "smpte2084" or "arib-std-b67";
}
